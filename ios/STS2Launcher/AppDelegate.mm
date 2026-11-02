#import "AppDelegate.h"
#import "ViewController.h"
#import "IOSAssemblyBootstrapper.h"
#import <AVFoundation/AVFoundation.h>

@implementation AppDelegate

+ (AppDelegate *)sharedDelegate {
    return (AppDelegate *)[UIApplication sharedApplication].delegate;
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
    
    // Stage .NET BCL and bootstrap assemblies into application support directory
    NSError *stageError = nil;
    if (![IOSAssemblyBootstrapper stageAssembliesWithError:&stageError]) {
        NSLog(@"[STS2Launcher iOS] Warning: Assembly staging encountered issues: %@", stageError);
    }
    
    // Set up window and main view controller
    self.window = [[UIWindow alloc] initWithFrame:[UIScreen mainScreen].bounds];
    self.viewController = [[ViewController alloc] init];
    self.window.rootViewController = self.viewController;
    [self.window makeKeyAndVisible];
    
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
