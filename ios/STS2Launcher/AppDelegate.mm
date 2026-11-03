#import "AppDelegate.h"
#import "IOSAssemblyBootstrapper.h"
#import <AVFoundation/AVFoundation.h>
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

@implementation AppDelegate

static AppDelegate *sSharedDelegate = nil;

+ (AppDelegate *)sharedDelegate {
    return sSharedDelegate;
}

+ (void)load {
    // Redirect stdout and stderr to Documents/app_output.log so all Godot/Mono/C# logs are accessible
    NSArray *docPaths = NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, YES);
    if (docPaths.count > 0) {
        NSString *logPath = [docPaths.firstObject stringByAppendingPathComponent:@"app_output.log"];
        freopen([logPath UTF8String], "a+", stdout);
        freopen([logPath UTF8String], "a+", stderr);
        setvbuf(stdout, NULL, _IONBF, 0);
        setvbuf(stderr, NULL, _IONBF, 0);
    }
    NSLog(@"[STS2Launcher iOS] Logging initialized; stdout/stderr redirected to Documents/app_output.log");

    sSharedDelegate = [[AppDelegate alloc] init];
    // Register as a Godot application delegate service so we receive lifecycle callbacks
    Class gdtClass = NSClassFromString(@"GDTApplicationDelegate");
    if (gdtClass && [gdtClass respondsToSelector:@selector(addService:)]) {
        [gdtClass performSelector:@selector(addService:) withObject:sSharedDelegate];
        NSLog(@"[STS2Launcher iOS] Registered AppDelegate as GDTApplicationDelegate service");
    }
}

- (BOOL)application:(UIApplication *)application didFinishLaunchingWithOptions:(NSDictionary *)launchOptions {
    NSLog(@"[STS2Launcher iOS] Application didFinishLaunchingWithOptions");
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
