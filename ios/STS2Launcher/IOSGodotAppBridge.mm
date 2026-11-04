#import "IOSGodotAppBridge.h"
#import "IOSAssemblyBootstrapper.h"
#import <UIKit/UIKit.h>
#import <Security/Security.h>
#import <sys/utsname.h>

extern "C" {

bool ios_share_text_file(const char *path) {
    if (!path) return false;
    
    NSString *filePath = [NSString stringWithUTF8String:path];
    NSURL *fileURL = [NSURL fileURLWithPath:filePath];
    
    dispatch_async(dispatch_get_main_queue(), ^{
        UIActivityViewController *activityVC = [[UIActivityViewController alloc] initWithActivityItems:@[fileURL] applicationActivities:nil];
        UIViewController *rootVC = [UIApplication sharedApplication].keyWindow.rootViewController;
        
        if (UI_USER_INTERFACE_IDIOM() == UIUserInterfaceIdiomPad) {
            activityVC.popoverPresentationController.sourceView = rootVC.view;
            activityVC.popoverPresentationController.sourceRect = CGRectMake(rootVC.view.bounds.size.width / 2, rootVC.view.bounds.size.height / 2, 0, 0);
        }
        
        [rootVC presentViewController:activityVC animated:YES completion:nil];
    });
    
    return true;
}

int64_t ios_get_usable_space_bytes(const char *path) {
    NSString *targetPath = path ? [NSString stringWithUTF8String:path] : NSHomeDirectory();
    NSDictionary *attrs = [[NSFileManager defaultManager] attributesOfFileSystemForPath:targetPath error:nil];
    if (attrs && attrs[NSFileSystemFreeSize]) {
        return [attrs[NSFileSystemFreeSize] longLongValue];
    }
    return -1;
}

const char *ios_get_device_diagnostics(void) {
    struct utsname systemInfo;
    uname(&systemInfo);
    NSString *deviceModel = [NSString stringWithCString:systemInfo.machine encoding:NSUTF8StringEncoding];
    NSString *osVersion = [[UIDevice currentDevice] systemVersion];
    
    NSString *diagnostics = [NSString stringWithFormat:@"Device: %@ | iOS: %@ | Memory: %llu MB",
                             deviceModel,
                             osVersion,
                             [NSProcessInfo processInfo].physicalMemory / (1024 * 1024)];
    
    return strdup([diagnostics UTF8String]);
}

const char *ios_get_version_name(void) {
    NSString *version = [[[NSBundle mainBundle] infoDictionary] objectForKey:@"CFBundleShortVersionString"];
    if (!version) {
        version = @"0.2.436";
    }
    return strdup([version UTF8String]);
}

void ios_set_keep_screen_on(bool keepOn) {
    dispatch_async(dispatch_get_main_queue(), ^{
        [UIApplication sharedApplication].idleTimerDisabled = keepOn;
    });
}

const char *ios_get_documents_dir(void) {
    NSArray *paths = NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, YES);
    return strdup([paths.firstObject UTF8String]);
}

const char *ios_get_user_data_dir(void) {
    return strdup([[IOSAssemblyBootstrapper userDataDirectory] UTF8String]);
}

void ios_get_safe_area_insets(float *top, float *bottom, float *left, float *right) {
    if (@available(iOS 11.0, *)) {
        UIWindow *window = [UIApplication sharedApplication].keyWindow;
        if (!window && [UIApplication sharedApplication].windows.count > 0) {
            window = [UIApplication sharedApplication].windows.firstObject;
        }
        
        if (window) {
            UIEdgeInsets insets = window.safeAreaInsets;
            if (top) *top = (float)insets.top;
            if (bottom) *bottom = (float)insets.bottom;
            if (left) *left = (float)insets.left;
            if (right) *right = (float)insets.right;
            return;
        }
    }
    
    if (top) *top = 0.0f;
    if (bottom) *bottom = 0.0f;
    if (left) *left = 0.0f;
    if (right) *right = 0.0f;
}

void ios_restart_app(void) {
    dispatch_async(dispatch_get_main_queue(), ^{
        // On iOS, an app cannot restart itself directly; exiting allows iOS/SideStore/user to relaunch.
        NSLog(@"[STS2Launcher iOS] Restart requested; terminating application.");
        exit(0);
    });
}

bool ios_get_or_create_device_key(uint8_t *key_out, int key_len) {
    if (!key_out || key_len <= 0) return false;
    
    NSString *service = @"com.game.sts2launcher";
    NSString *account = @"steam_credential_key";
    
    NSDictionary *query = @{
        (__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService: service,
        (__bridge id)kSecAttrAccount: account,
        (__bridge id)kSecReturnData: @YES,
        (__bridge id)kSecMatchLimit: (__bridge id)kSecMatchLimitOne
    };
    
    CFTypeRef dataTypeRef = NULL;
    OSStatus status = SecItemCopyMatching((__bridge CFDictionaryRef)query, &dataTypeRef);
    if (status == errSecSuccess) {
        NSData *keyData = (__bridge_transfer NSData *)dataTypeRef;
        if (keyData.length == (NSUInteger)key_len) {
            memcpy(key_out, keyData.bytes, key_len);
            return true;
        }
    }
    
    // Generate fresh cryptographically secure random bytes
    NSMutableData *newKey = [NSMutableData dataWithLength:key_len];
    if (SecRandomCopyBytes(kSecRandomDefault, key_len, newKey.mutableBytes) != 0) {
        return false;
    }
    
    // Delete stale item if one existed with wrong length
    if (status == errSecSuccess) {
        NSDictionary *deleteQuery = @{
            (__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
            (__bridge id)kSecAttrService: service,
            (__bridge id)kSecAttrAccount: account
        };
        SecItemDelete((__bridge CFDictionaryRef)deleteQuery);
    }
    
    NSDictionary *addQuery = @{
        (__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService: service,
        (__bridge id)kSecAttrAccount: account,
        (__bridge id)kSecValueData: newKey,
        (__bridge id)kSecAttrAccessible: (__bridge id)kSecAttrAccessibleAfterFirstUnlock
    };
    
    status = SecItemAdd((__bridge CFDictionaryRef)addQuery, NULL);
    if (status == errSecSuccess || status == errSecDuplicateItem) {
        memcpy(key_out, newKey.bytes, key_len);
        return true;
    }
    
    NSLog(@"[STS2Launcher iOS] SecItemAdd warning status: %d", (int)status);
    memcpy(key_out, newKey.bytes, key_len);
    return false;
}

}
