#import "AppDelegate.h"
#import "IOSAssemblyBootstrapper.h"
#import <AVFoundation/AVFoundation.h>

// Required by Godot 4.5.1 iOS static library (register_ios_api / unregister_ios_api)
void godot_apple_embedded_plugins_initialize() {}
void godot_apple_embedded_plugins_deinitialize() {}

@implementation AppDelegate

static AppDelegate *sSharedDelegate = nil;

+ (AppDelegate *)sharedDelegate {
    return sSharedDelegate;
}

+ (void)load {
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
    
    return YES;
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
}

- (void)applicationWillTerminate:(UIApplication *)application {
    NSLog(@"[STS2Launcher iOS] applicationWillTerminate");
}

@end
