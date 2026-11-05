#import "AppDelegate.h"
#import "IOSAssemblyBootstrapper.h"
#import <AVFoundation/AVFoundation.h>
#import <objc/runtime.h>
#include <stdint.h>

// Required by Godot 4.5.1 iOS static library (register_ios_api / unregister_ios_api)
void godot_apple_embedded_plugins_initialize() {}
void godot_apple_embedded_plugins_deinitialize() {}

// Declared in Godot 4.5.1 drivers/apple_embedded/os_apple_embedded.mm
extern void register_dynamic_symbol(char *name, void *address);
extern void add_apple_embedded_platform_init_callback(void (*cb)());

extern "C" void fmod_library_init();
extern "C" void spine_godot_library_init();

static void sts2_register_gdextension_symbols() {
    if (&fmod_library_init) {
        register_dynamic_symbol((char *)"fmod_library_init", (void *)fmod_library_init);
    }
    if (&spine_godot_library_init) {
        register_dynamic_symbol((char *)"spine_godot_library_init", (void *)spine_godot_library_init);
    }
}

struct STS2GDExtensionSymbolRegistrar {
    STS2GDExtensionSymbolRegistrar() {
        add_apple_embedded_platform_init_callback(sts2_register_gdextension_symbols);
    }
};
static STS2GDExtensionSymbolRegistrar sSTS2GDExtensionSymbolRegistrar;

// Required by utopia-rise/fmod-gdextension on iOS (FmodServer::load_all_plugins)
extern "C" __attribute__((visibility("default"))) __attribute__((used))
uint32_t* load_all_fmod_plugins(void* p_interface, uint32_t* r_count) {
    if (r_count) {
        *r_count = 0;
    }
    return nullptr;
}

// ---- JIT capability probe (diagnostic) ----
// Reports to the log whether this process can execute freshly written code, using
// the same strategies Mono might use. A fault is caught so the probe can't crash the app.
#include <sys/mman.h>
#include <setjmp.h>
#include <signal.h>
#include <pthread.h>
#include <libkern/OSCacheControl.h>
#include <sys/sysctl.h>
#include <sys/types.h>
#include <sys/proc.h>
#include <errno.h>
#include <string.h>
#include <stdio.h>
#include <dlfcn.h>
#include <unistd.h>

static sigjmp_buf sJitProbeJmp;
static void sts2_jit_probe_handler(int sig) { siglongjmp(sJitProbeJmp, sig); }

// mov w0,#42 ; ret
static const uint32_t kProbeCode[2] = { 0x52800540, 0xd65f03c0 };

// mode 0: plain RWX mmap. mode 1: RW then mprotect(RX). mode 2: MAP_JIT + write-protect toggle.
static bool sts2_jit_probe_case(int mode, const char *name) {
    struct sigaction sa, oldSegv, oldBus;
    memset(&sa, 0, sizeof(sa));
    sa.sa_handler = sts2_jit_probe_handler;
    sigemptyset(&sa.sa_mask);
    sigaction(SIGSEGV, &sa, &oldSegv);
    sigaction(SIGBUS, &sa, &oldBus);

    bool ok = false;
    int sig = sigsetjmp(sJitProbeJmp, 1);
    if (sig != 0) {
        printf("[JITProbe] %s: FAULT signal %d (errno=%d)\n", name, sig, errno);
        ok = false;
    } else {
        typedef void (*jit_protect_fn)(int);
        jit_protect_fn jitProtect = (jit_protect_fn)dlsym(RTLD_DEFAULT, "pthread_jit_write_protect_np");
        if (mode == 2 && !jitProtect) printf("[JITProbe] %s: pthread_jit_write_protect_np not found\n", name);
        size_t len = 16384;
        int prot = (mode == 0 || mode == 2) ? (PROT_READ | PROT_WRITE | PROT_EXEC) : (PROT_READ | PROT_WRITE);
        int flags = MAP_PRIVATE | MAP_ANON | (mode == 2 ? MAP_JIT : 0);
        void *p = mmap(NULL, len, prot, flags, -1, 0);
        if (p == MAP_FAILED) {
            printf("[JITProbe] %s: mmap FAILED errno=%d (%s)\n", name, errno, strerror(errno));
            ok = false;
        } else {
            if (mode == 2 && jitProtect) jitProtect(0);
            memcpy(p, kProbeCode, sizeof(kProbeCode));
            if (mode == 2 && jitProtect) jitProtect(1);
            sys_icache_invalidate(p, sizeof(kProbeCode));
            if (mode == 1) {
                int rc = mprotect(p, len, PROT_READ | PROT_EXEC);
                if (rc != 0) printf("[JITProbe] %s: mprotect RX FAILED errno=%d (%s)\n", name, errno, strerror(errno));
            }
            int r = ((int (*)(void))p)();
            printf("[JITProbe] %s: OK, executed generated code, returned %d\n", name, r);
            munmap(p, len);
            ok = (r == 42);
        }
    }
    sigaction(SIGSEGV, &oldSegv, NULL);
    sigaction(SIGBUS, &oldBus, NULL);
    return ok;
}

static bool sts2_run_jit_probe() {
    int mib[4] = { CTL_KERN, KERN_PROC, KERN_PROC_PID, (int)getpid() };
    struct kinfo_proc kp; size_t sz = sizeof(kp); memset(&kp, 0, sizeof(kp));
    int traced = (sysctl(mib, 4, &kp, &sz, NULL, 0) == 0) ? ((kp.kp_proc.p_flag & P_TRACED) ? 1 : 0) : -1;
    printf("[JITProbe] pid=%d P_TRACED(debugger attached)=%d\n", getpid(), traced);
    bool rwxOk = sts2_jit_probe_case(0, "plain RWX mmap");
    bool rxOk = sts2_jit_probe_case(1, "RW mmap + mprotect(RX)");
    bool mapJitOk = sts2_jit_probe_case(2, "MAP_JIT + pthread_jit_write_protect_np");
    bool anyJitOk = rwxOk || rxOk || mapJitOk;
    printf("[JITProbe] Overall JIT capability: %s (traced=%d, rwx=%d, rx=%d, mapJit=%d)\n",
           anyJitOk ? "AVAILABLE" : "UNAVAILABLE", traced, rwxOk, rxOk, mapJitOk);
    return anyJitOk;
}

static NSArray *sActiveGodotCmdline = nil;

static NSArray *sts2_determine_active_godot_cmdline() {
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        NSString *userData = [IOSAssemblyBootstrapper userDataDirectory];
        NSString *launchMarkerPath = [userData stringByAppendingPathComponent:@"pending_game_launch.json"];
        NSFileManager *fm = [NSFileManager defaultManager];
        
        if ([fm fileExistsAtPath:launchMarkerPath]) {
            NSData *data = [NSData dataWithContentsOfFile:launchMarkerPath];
            if (data) {
                NSError *error = nil;
                NSDictionary *dict = [NSJSONSerialization JSONObjectWithData:data options:0 error:&error];
                NSString *pckPath = dict[@"pck_path"];
                
                // Immediately remove marker to prevent infinite crash loops
                [fm removeItemAtPath:launchMarkerPath error:nil];
                
                if (pckPath && [fm fileExistsAtPath:pckPath]) {
                    NSLog(@"[STS2Launcher iOS] Bootstrapping game from pending launch marker: %@", pckPath);
                    setenv("STS2_LAUNCHER_BOOTSTRAP", "0", 1);
                    setenv("STS2_AUTO_LAUNCH_GAME", "1", 1);
                    setenv("STS2_ACTIVE_PCK_PATH", [pckPath UTF8String], 1);
                    sActiveGodotCmdline = @[ @"--main-pack", pckPath ];
                    return;
                } else {
                    NSLog(@"[STS2Launcher iOS] Pending launch PCK missing or invalid: %@; falling back to launcher", pckPath);
                }
            } else {
                [fm removeItemAtPath:launchMarkerPath error:nil];
            }
        }
        
        NSString *bundlePath = [[NSBundle mainBundle] bundlePath];
        NSString *bootstrapPath = [[NSBundle mainBundle] pathForResource:@"bootstrap" ofType:@"pck"];
        if (!bootstrapPath || ![fm fileExistsAtPath:bootstrapPath]) {
            bootstrapPath = [bundlePath stringByAppendingPathComponent:@"bootstrap.pck"];
        }
        if (!bootstrapPath || ![fm fileExistsAtPath:bootstrapPath]) {
            bootstrapPath = [bundlePath stringByAppendingPathComponent:@"STS2Launcher.pck"];
        }
        
        BOOL exists = [fm fileExistsAtPath:bootstrapPath];
        unsigned long long fileSize = 0;
        if (exists) {
            fileSize = [[fm attributesOfItemAtPath:bootstrapPath error:nil] fileSize];
        }
        NSLog(@"[STS2Launcher iOS] Bootstrapping launcher UI: %@ (exists=%d, size=%llu bytes)", bootstrapPath, exists, fileSize);
        
        setenv("STS2_LAUNCHER_BOOTSTRAP", "1", 1);
        setenv("STS2_AUTO_LAUNCH_GAME", "0", 1);
        sActiveGodotCmdline = @[ @"--main-pack", bootstrapPath ];
    });
    return sActiveGodotCmdline;
}

@interface NSBundle (STS2CmdlineSwizzle)
@end

@implementation NSBundle (STS2CmdlineSwizzle)

- (NSDictionary *)sts2_infoDictionary {
    NSDictionary *original = [self sts2_infoDictionary];
    if (self != [NSBundle mainBundle]) {
        return original;
    }
    NSMutableDictionary *modified = [original mutableCopy];
    if (!modified) {
        modified = [NSMutableDictionary dictionary];
    }
    NSArray *cmdline = sts2_determine_active_godot_cmdline();
    if (cmdline) {
        modified[@"godot_cmdline"] = cmdline;
    }
    return modified;
}

- (id)sts2_objectForInfoDictionaryKey:(NSString *)key {
    if (self == [NSBundle mainBundle] && [key isEqualToString:@"godot_cmdline"]) {
        NSArray *cmdline = sts2_determine_active_godot_cmdline();
        if (cmdline) {
            return cmdline;
        }
    }
    return [self sts2_objectForInfoDictionaryKey:key];
}

@end

@implementation AppDelegate

static AppDelegate *sSharedDelegate = nil;

+ (AppDelegate *)sharedDelegate {
    return sSharedDelegate;
}

+ (void)load {
    // 1. Redirect stdout and stderr to Documents/app_output.log so all Godot/Mono/C# logs are accessible
    NSArray *docPaths = NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, YES);
    if (docPaths.count > 0) {
        NSString *logPath = [docPaths.firstObject stringByAppendingPathComponent:@"app_output.log"];
        freopen([logPath UTF8String], "a+", stdout);
        freopen([logPath UTF8String], "a+", stderr);
        setvbuf(stdout, NULL, _IONBF, 0);
        setvbuf(stderr, NULL, _IONBF, 0);
    }
    NSLog(@"[STS2Launcher iOS] Logging initialized; stdout/stderr redirected to Documents/app_output.log");

    // 2. Change current working directory to bundle root so relative resource lookups succeed
    NSString *bundlePath = [[NSBundle mainBundle] bundlePath];
    if (bundlePath) {
        chdir([bundlePath UTF8String]);
        NSLog(@"[STS2Launcher iOS] Changed working directory to bundle: %@", bundlePath);
    }

    // 3. Swizzle NSBundle to inject dynamic godot_cmdline (--main-pack)
    Method origInfo = class_getInstanceMethod([NSBundle class], @selector(infoDictionary));
    Method swizInfo = class_getInstanceMethod([NSBundle class], @selector(sts2_infoDictionary));
    if (origInfo && swizInfo) {
        method_exchangeImplementations(origInfo, swizInfo);
    }
    
    Method origKey = class_getInstanceMethod([NSBundle class], @selector(objectForInfoDictionaryKey:));
    Method swizKey = class_getInstanceMethod([NSBundle class], @selector(sts2_objectForInfoDictionaryKey:));
    if (origKey && swizKey) {
        method_exchangeImplementations(origKey, swizKey);
    }
    
    // 4. Evaluate active cmdline and set environment variables immediately
    sts2_determine_active_godot_cmdline();

    // 5. Initialize shared delegate and execute JIT / audio session configuration immediately in +load
    sSharedDelegate = [[AppDelegate alloc] init];
    [sSharedDelegate ensureJitAndAudioConfigured];
}

- (void)ensureJitAndAudioConfigured {
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        NSLog(@"[STS2Launcher iOS] Configuring JIT and audio session before engine launch...");
        setenv("STS2_LAUNCHER_BOOTSTRAP", "1", 0);

        bool jitAvailable = sts2_run_jit_probe();
        if (jitAvailable) {
            NSLog(@"[STS2Launcher iOS] JIT is active; configuring Mono for full JIT execution");
            setenv("STS2_MONO_JIT", "1", 1);
        } else {
            NSLog(@"[STS2Launcher iOS] JIT is unavailable; falling back to Mono interpreter mode");
            unsetenv("STS2_MONO_JIT");
        }
        
        // Configure audio session for FMOD / game audio
        NSError *audioError = nil;
        AVAudioSession *audioSession = [AVAudioSession sharedInstance];
        [audioSession setCategory:AVAudioSessionCategoryPlayback
                      withOptions:AVAudioSessionCategoryOptionMixWithOthers
                            error:&audioError];
        if (audioError) {
            NSLog(@"[STS2Launcher iOS] Warning: Failed to set audio session category: %@", audioError);
        }
        [audioSession setActive:YES error:nil];

        [[NSNotificationCenter defaultCenter] addObserver:self
                                                 selector:@selector(handleAudioSessionInterruption:)
                                                     name:AVAudioSessionInterruptionNotification
                                                   object:audioSession];
    });
}

- (BOOL)application:(UIApplication *)application willFinishLaunchingWithOptions:(NSDictionary *)launchOptions {
    NSLog(@"[STS2Launcher iOS] Application willFinishLaunchingWithOptions");
    [self ensureJitAndAudioConfigured];
    return YES;
}

- (BOOL)application:(UIApplication *)application didFinishLaunchingWithOptions:(NSDictionary *)launchOptions {
    NSLog(@"[STS2Launcher iOS] Application didFinishLaunchingWithOptions");
    [self ensureJitAndAudioConfigured];
    return YES;
}

- (void)handleAudioSessionInterruption:(NSNotification *)notification {
    NSNumber *interruptionType = notification.userInfo[AVAudioSessionInterruptionTypeKey];
    if (!interruptionType) {
        return;
    }
    if (interruptionType.unsignedIntegerValue == AVAudioSessionInterruptionTypeEnded) {
        [[AVAudioSession sharedInstance] setActive:YES error:nil];
    }
}

- (void)applicationWillResignActive:(UIApplication *)application {
    NSLog(@"[STS2Launcher iOS] applicationWillResignActive");
}

- (void)applicationDidEnterBackground:(UIApplication *)application {
    NSLog(@"[STS2Launcher iOS] applicationDidEnterBackground");
}

- (void)applicationWillEnterForeground:(UIApplication *)application {
    NSLog(@"[STS2Launcher iOS] applicationWillEnterForeground");
}

- (void)applicationDidBecomeActive:(UIApplication *)application {
    NSLog(@"[STS2Launcher iOS] applicationDidBecomeActive");
    [[AVAudioSession sharedInstance] setActive:YES error:nil];
}

- (void)applicationWillTerminate:(UIApplication *)application {
    NSLog(@"[STS2Launcher iOS] applicationWillTerminate");
}

@end
