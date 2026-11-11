#import "IOSAssemblyBootstrapper.h"
#import <CommonCrypto/CommonDigest.h>

static NSData *STS2Sha256AtPath(NSString *path, NSError **error) {
    NSData *data = [NSData dataWithContentsOfFile:path options:NSDataReadingMappedIfSafe error:error];
    if (!data) return nil;
    unsigned char digest[CC_SHA256_DIGEST_LENGTH];
    CC_SHA256(data.bytes, (CC_LONG)data.length, digest);
    return [NSData dataWithBytes:digest length:sizeof(digest)];
}

static BOOL STS2CopyFileAtomicallyIfChanged(NSString *sourcePath, NSString *destinationPath, NSError **error) {
    NSFileManager *fm = [NSFileManager defaultManager];
    NSData *sourceHash = STS2Sha256AtPath(sourcePath, error);
    if (!sourceHash) return NO;

    if ([fm fileExistsAtPath:destinationPath]) {
        NSData *destinationHash = STS2Sha256AtPath(destinationPath, error);
        if (!destinationHash) return NO;
        if ([sourceHash isEqualToData:destinationHash]) return YES;
    }

    NSString *temporaryPath = [destinationPath stringByAppendingFormat:@".staging.%@", [[NSUUID UUID] UUIDString]];
    if (![fm copyItemAtPath:sourcePath toPath:temporaryPath error:error]) return NO;

    BOOL succeeded = NO;
    if ([fm fileExistsAtPath:destinationPath]) {
        succeeded = [fm replaceItemAtURL:[NSURL fileURLWithPath:destinationPath]
                         withItemAtURL:[NSURL fileURLWithPath:temporaryPath]
                        backupItemName:nil
                               options:0
                      resultingItemURL:nil
                                  error:error];
    } else {
        succeeded = [fm moveItemAtPath:temporaryPath toPath:destinationPath error:error];
    }

    if (!succeeded) [fm removeItemAtPath:temporaryPath error:nil];
    return succeeded;
}

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

+ (NSString *)documentsPublishDirectory {
    NSArray *paths = NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, YES);
    NSString *base = paths.firstObject;
    NSString *publishDir = [base stringByAppendingPathComponent:@".godot/mono/publish/arm64"];
    
    [[NSFileManager defaultManager] createDirectoryAtPath:publishDir
                              withIntermediateDirectories:YES
                                               attributes:nil
                                                    error:nil];
    return publishDir;
}

+ (BOOL)stageAssembliesWithError:(NSError **)error {
    NSFileManager *fm = [NSFileManager defaultManager];
    NSString *bundlePath = [[NSBundle mainBundle] resourcePath];
    NSString *targetPublish = [[[self userDataDirectory] stringByAppendingPathComponent:@".godot/mono/publish"]
        stringByAppendingPathComponent:@"arm64"];
    NSString *publishParent = [targetPublish stringByDeletingLastPathComponent];
    if (![fm createDirectoryAtPath:publishParent withIntermediateDirectories:YES attributes:nil error:error]) return NO;
    NSString *bclSource = [bundlePath stringByAppendingPathComponent:@"dotnet_bcl"];
    NSString *stagedAssembliesSource = [bundlePath stringByAppendingPathComponent:@"assemblies"];
    if (![fm fileExistsAtPath:bclSource isDirectory:nil]
        || ![fm fileExistsAtPath:stagedAssembliesSource isDirectory:nil]) {
        if (error) *error = [NSError errorWithDomain:@"STS2AssemblyBootstrapper" code:1
            userInfo:@{NSLocalizedDescriptionKey: @"The app bundle is missing dotnet_bcl or assemblies."}];
        return NO;
    }

    NSArray<NSString *> *bclFiles = [fm contentsOfDirectoryAtPath:bclSource error:error];
    if (!bclFiles) return NO;
    NSArray<NSString *> *requiredAssemblies = @[
        @"STS2Mobile.dll", @"SteamKit2.dll", @"0Harmony.dll", @"MonoMod.Core.dll",
        @"MonoMod.Utils.dll", @"MonoMod.Backports.dll", @"MonoMod.ILHelpers.dll",
        @"MonoMod.Iced.dll", @"Mono.Cecil.dll", @"protobuf-net.dll", @"protobuf-net.Core.dll",
        @"System.IO.Hashing.dll", @"System.Private.CoreLib.dll",
        @"System.Runtime.CompilerServices.Unsafe.dll", @"ZstdSharp.dll", @"GodotSharp.dll"
    ];

    // Validate the complete bootstrap input set before changing any staged file.
    for (NSString *assemblyName in requiredAssemblies) {
        NSString *assemblySource = [stagedAssembliesSource stringByAppendingPathComponent:assemblyName];
        NSString *bclAssemblySource = [bclSource stringByAppendingPathComponent:assemblyName];
        if (![fm fileExistsAtPath:assemblySource] && ![fm fileExistsAtPath:bclAssemblySource]) {
            if (error) *error = [NSError errorWithDomain:@"STS2AssemblyBootstrapper" code:2
                userInfo:@{NSLocalizedDescriptionKey: [NSString stringWithFormat:@"Required bundled assembly is missing: %@", assemblyName]}];
            return NO;
        }
    }

    // Recover an interrupted prior activation before preparing another complete
    // generation. A generation is activated only after every input is copied
    // and the required runtime set is present.
    NSString *previousPublish = [targetPublish stringByAppendingString:@".previous"];
    if (![fm fileExistsAtPath:targetPublish] && [fm fileExistsAtPath:previousPublish]) {
        if (![fm moveItemAtPath:previousPublish toPath:targetPublish error:error]) return NO;
    } else if ([fm fileExistsAtPath:targetPublish] && [fm fileExistsAtPath:previousPublish]) {
        if (![fm removeItemAtPath:previousPublish error:error]) return NO;
    }

    NSString *stagingPublish = [targetPublish stringByAppendingFormat:@".staging.%@", [[NSUUID UUID] UUIDString]];
    if (![fm createDirectoryAtPath:stagingPublish withIntermediateDirectories:YES attributes:nil error:error]) return NO;
    NSLog(@"[STS2Mobile iOS] Preparing complete assembly generation at: %@", stagingPublish);
    for (NSString *file in bclFiles) {
        // sts2.dll in dotnet_bcl is a metadata-only compile reference. Resolve
        // the active runtime assembly below, preserving the validated promoted
        // copy when one already exists.
        if ([file isEqualToString:@"sts2.dll"]) continue;
        NSString *sourcePath = [bclSource stringByAppendingPathComponent:file];
        NSString *destinationPath = [stagingPublish stringByAppendingPathComponent:file];
        if (!STS2CopyFileAtomicallyIfChanged(sourcePath, destinationPath, error)) {
            [fm removeItemAtPath:stagingPublish error:nil];
            return NO;
        }
    }
    
    for (NSString *assemblyName in requiredAssemblies) {
        NSString *assemblySource = [stagedAssembliesSource stringByAppendingPathComponent:assemblyName];
        NSString *bclAssemblySource = [bclSource stringByAppendingPathComponent:assemblyName];
        NSString *sourcePath = [fm fileExistsAtPath:assemblySource] ? assemblySource : bclAssemblySource;
        NSString *destinationPath = [stagingPublish stringByAppendingPathComponent:assemblyName];
        if (!STS2CopyFileAtomicallyIfChanged(sourcePath, destinationPath, error)) {
            [fm removeItemAtPath:stagingPublish error:nil];
            return NO;
        }
    }

    NSString *gameAssemblyName = @"sts2.dll";
    NSString *activeGameAssembly = [targetPublish stringByAppendingPathComponent:gameAssemblyName];
    NSString *referenceGameAssembly = [bclSource stringByAppendingPathComponent:gameAssemblyName];
    NSString *gameAssemblySource = [fm fileExistsAtPath:activeGameAssembly] ? activeGameAssembly : referenceGameAssembly;
    if ([fm fileExistsAtPath:gameAssemblySource]
        && !STS2CopyFileAtomicallyIfChanged(gameAssemblySource,
            [stagingPublish stringByAppendingPathComponent:gameAssemblyName], error)) {
        [fm removeItemAtPath:stagingPublish error:nil];
        return NO;
    }

    for (NSString *assemblyName in requiredAssemblies) {
        if (![fm fileExistsAtPath:[stagingPublish stringByAppendingPathComponent:assemblyName]]) {
            if (error) *error = [NSError errorWithDomain:@"STS2AssemblyBootstrapper" code:3
                userInfo:@{NSLocalizedDescriptionKey: [NSString stringWithFormat:@"Assembly staging did not produce %@", assemblyName]}];
            [fm removeItemAtPath:stagingPublish error:nil];
            return NO;
        }
    }

    if ([fm fileExistsAtPath:targetPublish]
        && ![fm moveItemAtPath:targetPublish toPath:previousPublish error:error]) return NO;
    if (![fm moveItemAtPath:stagingPublish toPath:targetPublish error:error]) {
        if ([fm fileExistsAtPath:previousPublish] && ![fm fileExistsAtPath:targetPublish])
            [fm moveItemAtPath:previousPublish toPath:targetPublish error:nil];
        [fm removeItemAtPath:stagingPublish error:nil];
        return NO;
    }
    if ([fm fileExistsAtPath:previousPublish]) [fm removeItemAtPath:previousPublish error:nil];

    // 3. Ensure Documents/StS2Launcher exists for user-visible files (mods, saves, logs)
    NSArray *docPaths = NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, YES);
    NSString *docsDir = [docPaths.firstObject stringByAppendingPathComponent:@"StS2Launcher"];
    [fm createDirectoryAtPath:docsDir withIntermediateDirectories:YES attributes:nil error:nil];
    [fm createDirectoryAtPath:[docsDir stringByAppendingPathComponent:@"Mods"] withIntermediateDirectories:YES attributes:nil error:nil];
    
    NSLog(@"[STS2Mobile iOS] Assembly staging complete.");
    return YES;
}

@end
