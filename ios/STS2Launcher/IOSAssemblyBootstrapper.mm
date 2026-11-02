#import "IOSAssemblyBootstrapper.h"
#import <CommonCrypto/CommonDigest.h>

@implementation IOSAssemblyBootstrapper

+ (void)load {
    NSLog(@"[STS2Launcher iOS] Triggering early assembly staging in +load...");
    NSError *error = nil;
    if (![self stageAssembliesWithError:&error]) {
        NSLog(@"[STS2Launcher iOS] Warning: Early assembly staging encountered issues: %@", error);
    }
}

+ (NSString *)userDataDirectory {
    NSArray *paths = NSSearchPathForDirectoriesInDomains(NSApplicationSupportDirectory, NSUserDomainMask, YES);
    NSString *base = paths.firstObject;
    NSString *userData = [base stringByAppendingPathComponent:@"Godot/app_userdata/sts2"];
    
    [[NSFileManager defaultManager] createDirectoryAtPath:userData
                              withIntermediateDirectories:YES
                                               attributes:nil
                                                    error:nil];
    return userData;
}

+ (NSString *)publishDirectory {
    NSString *userData = [self userDataDirectory];
    NSString *publishDir = [userData stringByAppendingPathComponent:@".godot/mono/publish/arm64"];
    
    [[NSFileManager defaultManager] createDirectoryAtPath:publishDir
                              withIntermediateDirectories:YES
                                               attributes:nil
                                                    error:nil];
    return publishDir;
}

+ (BOOL)stageAssembliesWithError:(NSError **)error {
    NSFileManager *fm = [NSFileManager defaultManager];
    NSString *bundlePath = [[NSBundle mainBundle] resourcePath];
    NSString *targetPublish = [self publishDirectory];
    
    NSLog(@"[STS2Mobile iOS] Staging assemblies into: %@", targetPublish);
    
    // 1. Stage dotnet_bcl if present in bundle
    NSString *bclSource = [bundlePath stringByAppendingPathComponent:@"dotnet_bcl"];
    if ([fm fileExistsAtPath:bclSource]) {
        NSArray *bclFiles = [fm contentsOfDirectoryAtPath:bclSource error:error];
        for (NSString *file in bclFiles) {
            NSString *srcFile = [bclSource stringByAppendingPathComponent:file];
            NSString *dstFile = [targetPublish stringByAppendingPathComponent:file];
            
            if (![fm fileExistsAtPath:dstFile]) {
                [fm copyItemAtPath:srcFile toPath:dstFile error:nil];
            }
        }
    }
    
    // 2. Stage bootstrap assemblies packaged in bundle root or publish directory
    NSString *stagedAssembliesSource = [bundlePath stringByAppendingPathComponent:@"assemblies"];
    if (![fm fileExistsAtPath:stagedAssembliesSource]) {
        stagedAssembliesSource = bundlePath;
    }
    
    NSArray *requiredAssemblies = @[
        @"STS2Mobile.dll",
        @"SteamKit2.dll",
        @"0Harmony.dll",
        @"MonoMod.Core.dll",
        @"MonoMod.Utils.dll",
        @"MonoMod.Backports.dll",
        @"MonoMod.ILHelpers.dll",
        @"MonoMod.Iced.dll",
        @"Mono.Cecil.dll",
        @"protobuf-net.dll",
        @"protobuf-net.Core.dll",
        @"System.IO.Hashing.dll",
        @"System.Private.CoreLib.dll",
        @"ZstdSharp.dll",
        @"GodotSharp.dll"
    ];
    
    for (NSString *assemblyName in requiredAssemblies) {
        NSString *srcFile = [stagedAssembliesSource stringByAppendingPathComponent:assemblyName];
        NSString *dstFile = [targetPublish stringByAppendingPathComponent:assemblyName];
        
        if ([fm fileExistsAtPath:srcFile]) {
            if ([fm fileExistsAtPath:dstFile]) {
                // If source is newer or different, replace it
                NSDictionary *srcAttr = [fm attributesOfItemAtPath:srcFile error:nil];
                NSDictionary *dstAttr = [fm attributesOfItemAtPath:dstFile error:nil];
                if (![srcAttr[NSFileSize] isEqualToNumber:dstAttr[NSFileSize]]) {
                    [fm removeItemAtPath:dstFile error:nil];
                    [fm copyItemAtPath:srcFile toPath:dstFile error:nil];
                }
            } else {
                [fm copyItemAtPath:srcFile toPath:dstFile error:nil];
            }
        }
    }
    
    // 3. Ensure Documents/StS2Launcher exists for user-visible files (mods, saves, logs)
    NSArray *docPaths = NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, YES);
    NSString *docsDir = [docPaths.firstObject stringByAppendingPathComponent:@"StS2Launcher"];
    [fm createDirectoryAtPath:docsDir withIntermediateDirectories:YES attributes:nil error:nil];
    [fm createDirectoryAtPath:[docsDir stringByAppendingPathComponent:@"Mods"] withIntermediateDirectories:YES attributes:nil error:nil];
    
    NSLog(@"[STS2Mobile iOS] Assembly staging complete.");
    return YES;
}

@end
