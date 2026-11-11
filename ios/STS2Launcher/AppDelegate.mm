#import "AppDelegate.h"
#import "IOSAssemblyBootstrapper.h"
#import <AVFoundation/AVFoundation.h>
#import <objc/runtime.h>
#include <stdint.h>
#include <exception>

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
#include <sys/ucontext.h>
#include <execinfo.h>
#include <errno.h>
#include <string.h>
#include <stdio.h>
#include <dlfcn.h>
#include <unistd.h>

static void sts2_crash_signal_handler(int sig, siginfo_t *info, void *uap) {
    signal(sig, SIG_DFL);

    fprintf(stderr, "\n========================================\n");
    fprintf(stderr, "[CRASH] Caught fatal signal %d (%s), si_code=%d, fault_addr=%p\n",
            sig, strsignal(sig), info ? info->si_code : 0, info ? info->si_addr : nullptr);

#if defined(__aarch64__)
    if (uap != nullptr) {
        ucontext_t *uc = (ucontext_t *)uap;
        if (uc->uc_mcontext != nullptr) {
            uint64_t pc = uc->uc_mcontext->__ss.__pc;
            uint64_t lr = uc->uc_mcontext->__ss.__lr;
            uint64_t fp = uc->uc_mcontext->__ss.__fp;
            uint64_t sp = uc->uc_mcontext->__ss.__sp;
            fprintf(stderr, "[CRASH] ARM64 Registers: pc=0x%llx lr=0x%llx fp=0x%llx sp=0x%llx\n",
                    (unsigned long long)pc, (unsigned long long)lr,
                    (unsigned long long)fp, (unsigned long long)sp);

            Dl_info dl_pc{}, dl_lr{};
            if (dladdr((void *)pc, &dl_pc) && dl_pc.dli_fname) {
                fprintf(stderr, "[CRASH] PC in %s (%s + 0x%llx)\n",
                        dl_pc.dli_fname,
                        dl_pc.dli_sname ? dl_pc.dli_sname : "<unknown>",
                        (unsigned long long)(pc - (uintptr_t)(dl_pc.dli_saddr ? dl_pc.dli_saddr : dl_pc.dli_fbase)));
            }
            if (dladdr((void *)lr, &dl_lr) && dl_lr.dli_fname) {
                fprintf(stderr, "[CRASH] LR in %s (%s + 0x%llx)\n",
                        dl_lr.dli_fname,
                        dl_lr.dli_sname ? dl_lr.dli_sname : "<unknown>",
                        (unsigned long long)(lr - (uintptr_t)(dl_lr.dli_saddr ? dl_lr.dli_saddr : dl_lr.dli_fbase)));
            }
        }
    }
#endif

    void *frames[64];
    int count = backtrace(frames, 64);
    fprintf(stderr, "[CRASH] Backtrace (%d frames):\n", count);
    for (int i = 0; i < count; i++) {
        Dl_info dl{};
        if (dladdr(frames[i], &dl) && dl.dli_fname) {
            uintptr_t base = (uintptr_t)(dl.dli_saddr ? dl.dli_saddr : dl.dli_fbase);
            fprintf(stderr, "  #%02d %p %s`%s + 0x%lx\n",
                    i, frames[i], dl.dli_fname,
                    dl.dli_sname ? dl.dli_sname : "<unknown>",
                    (unsigned long)((uintptr_t)frames[i] - base));
        } else {
            fprintf(stderr, "  #%02d %p\n", i, frames[i]);
        }
    }
    fprintf(stderr, "========================================\n");
    fflush(stderr);
    fflush(stdout);
    fsync(STDERR_FILENO);
    raise(sig);
}

static char sAltStack[131072]; // 128 KB dedicated alternate signal stack

static void sts2_setup_alt_stack() {
    stack_t ss;
    memset(&ss, 0, sizeof(ss));
    ss.ss_sp = sAltStack;
    ss.ss_size = sizeof(sAltStack);
    ss.ss_flags = 0;
    if (sigaltstack(&ss, nullptr) != 0) {
        fprintf(stderr, "[STS2Launcher iOS] Warning: sigaltstack failed: %d\n", errno);
    }
}

static void sts2_uncaught_objc_exception_handler(NSException *exception) {
    fprintf(stderr, "\n========================================\n");
    fprintf(stderr, "[CRASH] Uncaught NSException: %s\n", [[exception name] UTF8String] ?: "<unknown>");
    fprintf(stderr, "Reason: %s\n", [[exception reason] UTF8String] ?: "<none>");
    fprintf(stderr, "Call stack symbols:\n");
    for (NSString *symbol in [exception callStackSymbols]) {
        fprintf(stderr, "  %s\n", [symbol UTF8String]);
    }
    fprintf(stderr, "========================================\n");
    fflush(stderr);
    fflush(stdout);
    fsync(STDERR_FILENO);
}

static void sts2_cpp_terminate_handler() {
    fprintf(stderr, "\n========================================\n");
    fprintf(stderr, "[CRASH] std::terminate called!\n");
    void *frames[64];
    int count = backtrace(frames, 64);
    for (int i = 0; i < count; i++) {
        Dl_info dl{};
        if (dladdr(frames[i], &dl) && dl.dli_fname) {
            uintptr_t base = (uintptr_t)(dl.dli_saddr ? dl.dli_saddr : dl.dli_fbase);
            fprintf(stderr, "  #%02d %p %s`%s + 0x%lx\n",
                    i, frames[i], dl.dli_fname,
                    dl.dli_sname ? dl.dli_sname : "<unknown>",
                    (unsigned long)((uintptr_t)frames[i] - base));
        } else {
            fprintf(stderr, "  #%02d %p\n", i, frames[i]);
        }
    }
    fprintf(stderr, "========================================\n");
    fflush(stderr);
    fflush(stdout);
    fsync(STDERR_FILENO);
    abort();
}

static void sts2_install_crash_handlers() {
    sts2_setup_alt_stack();

    struct sigaction sa;
    memset(&sa, 0, sizeof(sa));
    sa.sa_sigaction = sts2_crash_signal_handler;
    sa.sa_flags = SA_SIGINFO | SA_ONSTACK;
    sigemptyset(&sa.sa_mask);

    sigaction(SIGSEGV, &sa, nullptr);
    sigaction(SIGABRT, &sa, nullptr);
    sigaction(SIGBUS, &sa, nullptr);
    sigaction(SIGILL, &sa, nullptr);
    sigaction(SIGFPE, &sa, nullptr);
    sigaction(SIGTRAP, &sa, nullptr);

    NSSetUncaughtExceptionHandler(&sts2_uncaught_objc_exception_handler);
    std::set_terminate(&sts2_cpp_terminate_handler);
}

extern "C" __attribute__((visibility("default"))) void sts2_reinstall_crash_handlers() {
    sts2_install_crash_handlers();
    fprintf(stderr, "[STS2Launcher iOS] Native crash signal handlers reinstalled.\n");
    fflush(stderr);
}

static sigjmp_buf sJitProbeJmp;
static void sts2_jit_probe_handler(int sig) { siglongjmp(sJitProbeJmp, sig); }

// mov w0,#42 ; ret
static const uint32_t kProbeCode[2] = { 0x52800540, 0xd65f03c0 };

typedef void (*MonoLogCallback)(const char *log_domain, const char *log_level, const char *message, int fatal, void *user_data);
typedef void (*mono_trace_set_log_handler_fn)(MonoLogCallback callback, void *user_data);
typedef void (*mono_trace_set_print_handler_fn)(void (*handler)(const char *string, int is_stdout));
typedef void (*mono_trace_set_printerr_handler_fn)(void (*handler)(const char *string, int is_stdout));

static void sts2_mono_log_callback(const char *log_domain, const char *log_level, const char *message, int fatal, void *user_data) {
    fprintf(stderr, "[Mono:%s:%s%s] %s\n",
            log_domain ? log_domain : "core",
            log_level ? log_level : "info",
            fatal ? ":FATAL" : "",
            message ? message : "");
    fflush(stderr);
}

static void sts2_mono_print_callback(const char *string, int is_stdout) {
    fprintf(stderr, "[Mono] %s\n", string ? string : "");
    fflush(stderr);
}

static void sts2_install_mono_log_hooks() {
    mono_trace_set_log_handler_fn set_log = (mono_trace_set_log_handler_fn)dlsym(RTLD_DEFAULT, "mono_trace_set_log_handler");
    if (set_log) {
        set_log(sts2_mono_log_callback, nullptr);
    }
    mono_trace_set_print_handler_fn set_print = (mono_trace_set_print_handler_fn)dlsym(RTLD_DEFAULT, "mono_trace_set_print_handler");
    if (set_print) {
        set_print(sts2_mono_print_callback);
    }
    mono_trace_set_printerr_handler_fn set_err = (mono_trace_set_printerr_handler_fn)dlsym(RTLD_DEFAULT, "mono_trace_set_printerr_handler");
    if (set_err) {
        set_err(sts2_mono_print_callback);
    }
}

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
    fflush(stdout);
    return anyJitOk;
}

extern "C" bool sts2_ios_is_jit_ready(int *r_pid, int *r_traced, int *r_cs_debugged) {
    sts2_install_mono_log_hooks();
    static bool sJitConfirmed = false;
    int pid = (int)getpid();
    int mib[4] = { CTL_KERN, KERN_PROC, KERN_PROC_PID, pid };
    struct kinfo_proc kp;
    size_t sz = sizeof(kp);
    memset(&kp, 0, sizeof(kp));
    int traced = (sysctl(mib, 4, &kp, &sz, NULL, 0) == 0) ? ((kp.kp_proc.p_flag & P_TRACED) ? 1 : 0) : 0;

    int cs_debugged = 0;
    typedef int (*csops_fn)(pid_t pid, unsigned int ops, void *useraddr, size_t usersize);
    static csops_fn s_csops = (csops_fn)dlsym(RTLD_DEFAULT, "csops");
    if (s_csops != nullptr) {
        uint32_t csflags = 0;
        if (s_csops(pid, 0, &csflags, sizeof(csflags)) == 0) {
            cs_debugged = (csflags & 0x10000000) ? 1 : 0;
        }
    }

    if (r_pid) {
        *r_pid = pid;
    }
    if (r_traced) {
        *r_traced = traced;
    }
    if (r_cs_debugged) {
        *r_cs_debugged = cs_debugged;
    }

    if (sJitConfirmed) {
        return true;
    }

    // CRITICAL: NEVER run an executable memory probe if no debugger is attached (traced == 0).
    // On iOS without CS_DEBUGGED/debugger, executing an unsigned mmap page causes AMFI to instantly
    // terminate the process with an uncatchable SIGKILL (Codesigning violation).
    if (traced != 0) {
        static bool sRanInitialProbe = false;
        if (!sRanInitialProbe) {
            sRanInitialProbe = true;
            bool jitWorking = sts2_run_jit_probe();
            if (jitWorking) {
                setenv("STS2_MONO_JIT", "1", 1);
                unsetenv("STS2_MONO_FORCE_INTERP");
                sJitConfirmed = true;
                NSLog(@"[STS2Launcher iOS] JIT confirmed working (P_TRACED=%d, CS_DEBUGGED=%d); STS2_MONO_JIT=1 enabled", traced, cs_debugged);
                return true;
            }
        }
    }

    // JIT is not available or not permitted; ensure Mono runs safely in Interpreter mode
    unsetenv("STS2_MONO_JIT");
    setenv("STS2_MONO_FORCE_INTERP", "1", 1);
    return false;
}

static NSArray *sActiveGodotCmdline = nil;

static BOOL sts2_hasGameSupportAssemblies(NSString *userData) {
    NSString *publishDir = [userData stringByAppendingPathComponent:@".godot/mono/publish/arm64"];
    NSFileManager *fm = [NSFileManager defaultManager];
    for (NSString *name in @[@"Steamworks.NET.dll", @"Sentry.dll"]) {
        if (![fm fileExistsAtPath:[publishDir stringByAppendingPathComponent:name]])
            return NO;
    }
    return YES;
}

static NSArray *sts2_determine_active_godot_cmdline() {
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        NSString *userData = [IOSAssemblyBootstrapper userDataDirectory];
        NSString *launchMarkerPath = [userData stringByAppendingPathComponent:@"pending_game_launch.json"];
        NSFileManager *fm = [NSFileManager defaultManager];

        NSArray *documentPaths = NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, YES);
        NSString *documents = documentPaths.firstObject;
        NSString *probeMarker = [documents stringByAppendingPathComponent:@"gameplay-property-probe.enabled"];
        if (documents && [fm fileExistsAtPath:probeMarker]) {
            NSString *bundlePath = [[NSBundle mainBundle] bundlePath];
            NSString *bootstrapPath = [[NSBundle mainBundle] pathForResource:@"bootstrap" ofType:@"pck"];
            if (!bootstrapPath || ![fm fileExistsAtPath:bootstrapPath]) {
                bootstrapPath = [bundlePath stringByAppendingPathComponent:@"bootstrap.pck"];
            }
            if (!bootstrapPath || ![fm fileExistsAtPath:bootstrapPath]) {
                bootstrapPath = [bundlePath stringByAppendingPathComponent:@"STS2Launcher.pck"];
            }
            if (![fm fileExistsAtPath:bootstrapPath]) {
                NSLog(@"[STS2Launcher iOS] Property-probe marker found, but bootstrap PCK is missing: %@", bootstrapPath);
                unsetenv("STS2_GAMEPLAY_PROPERTY_PROBE_ONLY");
            } else {
                NSLog(@"[STS2Launcher iOS] Running one-shot standalone property probe from %@", bootstrapPath);
                setenv("STS2_LAUNCHER_BOOTSTRAP", "1", 1);
                setenv("STS2_AUTO_LAUNCH_GAME", "0", 1);
                setenv("STS2_GAMEPLAY_PROPERTY_PROBE_ONLY", "1", 1);
                sActiveGodotCmdline = @[ @"--verbose", @"--main-pack", bootstrapPath ];
                return;
            }
        }
        unsetenv("STS2_GAMEPLAY_PROPERTY_PROBE_ONLY");
        
        // 1. Check for pending game launch marker written by standalone launcher
        if ([fm fileExistsAtPath:launchMarkerPath]) {
            NSData *data = [NSData dataWithContentsOfFile:launchMarkerPath];
            if (data) {
                NSError *error = nil;
                NSDictionary *dict = [NSJSONSerialization JSONObjectWithData:data options:0 error:&error];
                NSString *pckPath = dict[@"pck_path"];
                
                // Immediately remove marker to prevent infinite crash loops
                [fm removeItemAtPath:launchMarkerPath error:nil];
                
                if (pckPath && [fm fileExistsAtPath:pckPath]) {
                    if (!sts2_hasGameSupportAssemblies(userData)) {
                        NSLog(@"[STS2Launcher iOS] Required game support assemblies are missing; refusing direct game boot and opening launcher.");
                    } else {
                        NSLog(@"[STS2Launcher iOS] Bootstrapping game from pending launch marker: %@", pckPath);
                        setenv("STS2_LAUNCHER_BOOTSTRAP", "0", 1);
                        setenv("STS2_AUTO_LAUNCH_GAME", "1", 1);
                        setenv("STS2_ACTIVE_PCK_PATH", [pckPath UTF8String], 1);
                        sActiveGodotCmdline = @[ @"--verbose", @"--main-pack", pckPath ];
                        return;
                    }
                } else {
                    NSLog(@"[STS2Launcher iOS] Pending launch PCK missing or invalid: %@; falling back to launcher", pckPath);
                }
            } else {
                [fm removeItemAtPath:launchMarkerPath error:nil];
            }
        }
        
        // 2. If no pending launch marker, check current_runtime_slot.json for an authorized playable PCK
        NSString *runtimeSlotPath = [userData stringByAppendingPathComponent:@"current_runtime_slot.json"];
        if ([fm fileExistsAtPath:runtimeSlotPath]) {
            NSData *slotData = [NSData dataWithContentsOfFile:runtimeSlotPath];
            if (slotData) {
                NSDictionary *slotDict = [NSJSONSerialization JSONObjectWithData:slotData options:0 error:nil];
                if (slotDict && [slotDict[@"playable"] boolValue]) {
                    NSString *pckPath = slotDict[@"pckPath"];
                    if (pckPath && [fm fileExistsAtPath:pckPath]) {
                        if (!sts2_hasGameSupportAssemblies(userData)) {
                            NSLog(@"[STS2Launcher iOS] Required game support assemblies are missing; opening launcher instead of direct boot.");
                        } else {
                            NSLog(@"[STS2Launcher iOS] Found authorized runtime slot PCK at: %@; booting game directly.", pckPath);
                            setenv("STS2_LAUNCHER_BOOTSTRAP", "0", 1);
                            setenv("STS2_AUTO_LAUNCH_GAME", "1", 1);
                            setenv("STS2_ACTIVE_PCK_PATH", [pckPath UTF8String], 1);
                            sActiveGodotCmdline = @[ @"--verbose", @"--main-pack", pckPath ];
                            return;
                        }
                    }
                }
            }
        }
        
        // 3. Fallback check for installed game PCK on disk:
        //    a) Legacy public: ~/game/SlayTheSpire2.pck
        //    b) Branch cache: ~/game_versions/*/game/SlayTheSpire2.pck
        NSString *installedGamePck = [userData stringByAppendingPathComponent:@"game/SlayTheSpire2.pck"];
        if (![fm fileExistsAtPath:installedGamePck]) {
            NSString *versionsDir = [userData stringByAppendingPathComponent:@"game_versions"];
            NSArray *versionDirs = [fm contentsOfDirectoryAtPath:versionsDir error:nil];
            for (NSString *subDir in versionDirs) {
                NSString *candidate = [versionsDir stringByAppendingPathComponent:[NSString stringWithFormat:@"%@/game/SlayTheSpire2.pck", subDir]];
                if ([fm fileExistsAtPath:candidate]) {
                    installedGamePck = candidate;
                    break;
                }
            }
        }
        
        if ([fm fileExistsAtPath:installedGamePck]) {
            if (!sts2_hasGameSupportAssemblies(userData)) {
                NSLog(@"[STS2Launcher iOS] Installed game PCK found but Steamworks.NET.dll or Sentry.dll is missing; opening launcher for runtime preparation.");
            } else {
                NSLog(@"[STS2Launcher iOS] Found installed game PCK at: %@; booting game directly.", installedGamePck);
                setenv("STS2_LAUNCHER_BOOTSTRAP", "0", 1);
                setenv("STS2_AUTO_LAUNCH_GAME", "1", 1);
                setenv("STS2_ACTIVE_PCK_PATH", [installedGamePck UTF8String], 1);
                sActiveGodotCmdline = @[ @"--verbose", @"--main-pack", installedGamePck ];
                return;
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
        sActiveGodotCmdline = @[ @"--verbose", @"--main-pack", bootstrapPath ];
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
    // 1. Redirect stdout and stderr to Documents/app_output.log using shared fd so lines don't overwrite each other.
    // Rotate current log to app_output_prev.log so historical handoff/crash logs are preserved across app relaunches.
    NSArray *docPaths = NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, YES);
    if (docPaths.count > 0) {
        NSFileManager *fm = [NSFileManager defaultManager];
        NSString *logPath = [docPaths.firstObject stringByAppendingPathComponent:@"app_output.log"];
        NSString *prevLogPath = [docPaths.firstObject stringByAppendingPathComponent:@"app_output_prev.log"];
        
        if ([fm fileExistsAtPath:logPath]) {
            [fm removeItemAtPath:prevLogPath error:nil];
            [fm moveItemAtPath:logPath toPath:prevLogPath error:nil];
        }
        
        int logFd = open([logPath UTF8String], O_WRONLY | O_CREAT | O_TRUNC | O_APPEND, 0644);
        if (logFd >= 0) {
            dup2(logFd, STDOUT_FILENO);
            dup2(logFd, STDERR_FILENO);
            if (logFd != STDOUT_FILENO && logFd != STDERR_FILENO) {
                close(logFd);
            }
            setvbuf(stdout, NULL, _IONBF, 0);
            setvbuf(stderr, NULL, _IONBF, 0);
        }
    }
    sts2_install_crash_handlers();
    sts2_install_mono_log_hooks();
    setenv("MONO_LOG_LEVEL", "info", 0);
    setenv("MONO_LOG_MASK", "asm,type,dll", 0);
    setenv("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "1", 0);
    setenv("STS2_SKIP_JIT_WAIT", "1", 1);
    setenv("STS2_MONO_FORCE_INTERP", "1", 1);
    NSLog(@"[STS2Launcher iOS] Logging & crash signal handlers initialized (PID=%d); stdout/stderr -> Documents/app_output.log", getpid());

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

    // 5. Initialize shared delegate and configure audio session in +load (JIT is checked in GDTAppDelegateService)
    sSharedDelegate = [[AppDelegate alloc] init];
    [sSharedDelegate ensureJitAndAudioConfigured];
}

- (void)ensureJitAndAudioConfigured {
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        NSLog(@"[STS2Launcher iOS] Configuring audio session before engine launch...");
        setenv("STS2_LAUNCHER_BOOTSTRAP", "1", 0);
        
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
