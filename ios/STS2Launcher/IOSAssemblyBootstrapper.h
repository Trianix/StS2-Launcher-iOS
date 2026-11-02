#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

@interface IOSAssemblyBootstrapper : NSObject

/// Stages the .NET BCL and required STS2Mobile bootstrap assemblies from the app bundle
/// to the application's private data directory (~/Library/Application Support/.godot/mono/publish/arm64).
+ (BOOL)stageAssembliesWithError:(NSError **)error;

/// Returns the path to the directory where assemblies are published for the Godot Mono runtime.
+ (NSString *)publishDirectory;

/// Returns the root user data directory for StS2 on iOS (~/Library/Application Support/Godot/app_userdata/sts2 or Documents).
+ (NSString *)userDataDirectory;

@end

NS_ASSUME_NONNULL_END
