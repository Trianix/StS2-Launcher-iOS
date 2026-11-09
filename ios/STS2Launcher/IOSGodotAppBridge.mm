#import "IOSGodotAppBridge.h"
#import "IOSAssemblyBootstrapper.h"
#import <UIKit/UIKit.h>
#import <Security/Security.h>
#import <UserNotifications/UserNotifications.h>
#import <sys/utsname.h>
#import <unistd.h>
#import <fcntl.h>
#import <errno.h>

extern "C" {

IOS_BRIDGE_EXPORT bool ios_share_text_file(const char *path) {
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

IOS_BRIDGE_EXPORT int64_t ios_get_usable_space_bytes(const char *path) {
    NSString *targetPath = path ? [NSString stringWithUTF8String:path] : NSHomeDirectory();
    NSDictionary *attrs = [[NSFileManager defaultManager] attributesOfFileSystemForPath:targetPath error:nil];
    if (attrs && attrs[NSFileSystemFreeSize]) {
        return [attrs[NSFileSystemFreeSize] longLongValue];
    }
    return -1;
}

IOS_BRIDGE_EXPORT const char *ios_get_device_diagnostics(void) {
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

IOS_BRIDGE_EXPORT const char *ios_get_version_name(void) {
    NSString *version = [[[NSBundle mainBundle] infoDictionary] objectForKey:@"CFBundleShortVersionString"];
    if (!version) {
        version = @"0.2.436";
    }
    return strdup([version UTF8String]);
}

IOS_BRIDGE_EXPORT void ios_set_keep_screen_on(bool keepOn) {
    dispatch_async(dispatch_get_main_queue(), ^{
        [UIApplication sharedApplication].idleTimerDisabled = keepOn;
    });
}

IOS_BRIDGE_EXPORT const char *ios_get_documents_dir(void) {
    static NSString *docs = nil;
    if (!docs) {
        NSArray *paths = NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, YES);
        docs = paths.firstObject;
    }
    return [docs UTF8String];
}

IOS_BRIDGE_EXPORT const char *ios_get_user_data_dir(void) {
    static NSString *userData = nil;
    if (!userData) {
        userData = [IOSAssemblyBootstrapper userDataDirectory];
    }
    return [userData UTF8String];
}

IOS_BRIDGE_EXPORT void ios_get_safe_area_insets(float *top, float *bottom, float *left, float *right) {
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

IOS_BRIDGE_EXPORT void ios_restart_app(void) {
    dispatch_async(dispatch_get_main_queue(), ^{
        // On iOS, an app cannot restart itself directly; exiting allows iOS/SideStore/user to relaunch.
        NSLog(@"[STS2Launcher iOS] Restart requested; terminating application.");
        _exit(0);
    });
}

IOS_BRIDGE_EXPORT void ios_request_game_launch_and_restart(const char *pck_path, const char *source) {
    if (!pck_path) return;
    
    NSString *pckPathStr = [NSString stringWithUTF8String:pck_path];
    NSString *sourceStr = source ? [NSString stringWithUTF8String:source] : @"launcher";
    
    // 1. Write pending_game_launch.json
    NSString *userData = [IOSAssemblyBootstrapper userDataDirectory];
    NSString *launchMarkerPath = [userData stringByAppendingPathComponent:@"pending_game_launch.json"];
    
    NSDictionary *markerDict = @{
        @"pck_path": pckPathStr,
        @"source": sourceStr,
        @"timestamp": @((long long)[[NSDate date] timeIntervalSince1970])
    };
    
    NSError *jsonError = nil;
    NSData *jsonData = [NSJSONSerialization dataWithJSONObject:markerDict options:NSJSONWritingPrettyPrinted error:&jsonError];
    if (jsonData) {
        [jsonData writeToFile:launchMarkerPath atomically:YES];
        NSLog(@"[STS2Launcher iOS] Wrote pending game launch marker to: %@", launchMarkerPath);
    } else {
        NSLog(@"[STS2Launcher iOS] Error creating launch marker JSON: %@", jsonError);
    }
    
    // 2. Schedule local notification to alert the user that the game is ready
    if (@available(iOS 10.0, *)) {
        UNUserNotificationCenter *center = [UNUserNotificationCenter currentNotificationCenter];
        [center requestAuthorizationWithOptions:(UNAuthorizationOptionAlert | UNAuthorizationOptionSound)
                              completionHandler:^(BOOL granted, NSError * _Nullable error) {
            if (granted) {
                UNMutableNotificationContent *content = [[UNMutableNotificationContent alloc] init];
                content.title = @"Slay the Spire 2";
                content.body = @"Game is ready! Tap to launch.";
                content.sound = [UNNotificationSound defaultSound];
                
                UNTimeIntervalNotificationTrigger *trigger = [UNTimeIntervalNotificationTrigger triggerWithTimeInterval:0.2 repeats:NO];
                UNNotificationRequest *request = [UNNotificationRequest requestWithIdentifier:@"STS2GameReady" content:content trigger:trigger];
                [center addNotificationRequest:request withCompletionHandler:nil];
            }
        }];
    }

    // 3. Present confirmation alert on main thread so user understands why launcher exits and knows to reopen the app
    dispatch_async(dispatch_get_main_queue(), ^{
        UIWindow *keyWin = nil;
        if (@available(iOS 13.0, *)) {
            for (UIScene *scene in [UIApplication sharedApplication].connectedScenes) {
                if (scene.activationState == UISceneActivationStateForegroundActive && [scene isKindOfClass:[UIWindowScene class]]) {
                    for (UIWindow *w in ((UIWindowScene *)scene).windows) {
                        if (w.isKeyWindow) { keyWin = w; break; }
                    }
                }
            }
        }
        if (!keyWin) {
            keyWin = [UIApplication sharedApplication].keyWindow;
        }
        
        UIViewController *rootVC = keyWin.rootViewController;
        if (rootVC) {
            UIAlertController *alert = [UIAlertController alertControllerWithTitle:@"Game Files Ready"
                                                                           message:@"Preparation complete! Please tap Exit, then tap the Slay the Spire 2 app icon on your home screen to start the game."
                                                                    preferredStyle:UIAlertControllerStyleAlert];
            [alert addAction:[UIAlertAction actionWithTitle:@"Exit to Home Screen"
                                                      style:UIAlertActionStyleDefault
                                                    handler:^(UIAlertAction * _Nonnull action) {
                NSLog(@"[STS2Launcher iOS] User acknowledged game handoff; exiting launcher for game boot.");
                _exit(0);
            }]];
            [rootVC presentViewController:alert animated:YES completion:nil];
            
            // Safety timer: auto-exit after 30s if user ignores the dialog
            dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(30.0 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
                NSLog(@"[STS2Launcher iOS] Safety timeout reached; terminating launcher for game boot.");
                _exit(0);
            });
        } else {
            // Fallback if no view controller is visible
            dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(1.5 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
                NSLog(@"[STS2Launcher iOS] Launch handoff complete; terminating launcher for game boot.");
                _exit(0);
            });
        }
    });
}

IOS_BRIDGE_EXPORT bool ios_get_or_create_device_key(uint8_t *key_out, int key_len) {
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

// POSIX compatibility shims for MonoMod on Darwin/iOS
IOS_BRIDGE_EXPORT int pipe2(int fds[2], int flags) {
    int ret = pipe(fds);
    if (ret != 0) return ret;
    if (flags & 0x80000) { // O_CLOEXEC
        fcntl(fds[0], F_SETFD, FD_CLOEXEC);
        fcntl(fds[1], F_SETFD, FD_CLOEXEC);
    }
    return 0;
}

IOS_BRIDGE_EXPORT int* __errno_location(void) {
    return __error();
}

struct Sts2InterpMethod {
    void *method;
    Sts2InterpMethod *next_jit_code_hash;
    unsigned short *code;
    void *func;
    void *clauses;
    void **data_items;
    uint32_t *local_offsets;
    uint32_t *arg_offsets;
    uint32_t *clause_data_offsets;
    void *jit_call_info;
    void *jit_entry;
    void *llvmonly_unbox_entry;
    void *rtype;
    void **param_types;
    void *jinfo;
    void *ftndesc;
    void *ftndesc_unbox;
    void *del_info;
    uint32_t locals_size;
    uint32_t alloca_size;
    int num_clauses;
    int transformed;
    unsigned int param_count;
    unsigned int hasthis;
    uint32_t prof_flags;
    uint32_t code_type;
    int ref_slot_offset;
    int swift_error_offset;
    void *ref_slots;
    int32_t entry_count;
    Sts2InterpMethod *optimized_imethod;
    int *patchpoint_data;
    unsigned int init_locals : 1;
    unsigned int vararg : 1;
    unsigned int optimized : 1;
    unsigned int needs_thread_attach : 1;
    unsigned int is_invoke : 1;
    unsigned int is_verbose : 1;
};

extern Sts2InterpMethod* mono_interp_get_imethod(void *method);
extern void* mono_compile_method(void *method);
extern char* mono_method_full_name(void *method, int signature);

static void sts2_copy_interp_body(Sts2InterpMethod *dst_target, Sts2InterpMethod *src_body) {
    dst_target->method = src_body->method;
    dst_target->code = src_body->code;
    dst_target->func = src_body->func;
    dst_target->clauses = src_body->clauses;
    dst_target->data_items = src_body->data_items;
    dst_target->local_offsets = src_body->local_offsets;
    dst_target->arg_offsets = src_body->arg_offsets;
    dst_target->clause_data_offsets = src_body->clause_data_offsets;
    dst_target->jit_call_info = src_body->jit_call_info;
    dst_target->jit_entry = src_body->jit_entry;
    dst_target->llvmonly_unbox_entry = src_body->llvmonly_unbox_entry;
    dst_target->rtype = src_body->rtype;
    dst_target->param_types = src_body->param_types;
    dst_target->jinfo = src_body->jinfo;
    dst_target->ftndesc = src_body->ftndesc;
    dst_target->ftndesc_unbox = src_body->ftndesc_unbox;
    dst_target->del_info = src_body->del_info;
    dst_target->locals_size = src_body->locals_size;
    dst_target->alloca_size = src_body->alloca_size;
    dst_target->num_clauses = src_body->num_clauses;
    dst_target->param_count = src_body->param_count;
    dst_target->hasthis = src_body->hasthis;
    dst_target->prof_flags = src_body->prof_flags;
    dst_target->code_type = src_body->code_type;
    dst_target->ref_slot_offset = src_body->ref_slot_offset;
    dst_target->swift_error_offset = src_body->swift_error_offset;
    dst_target->ref_slots = src_body->ref_slots;
    dst_target->entry_count = 0;
    dst_target->optimized_imethod = nullptr;
    dst_target->patchpoint_data = nullptr;
    dst_target->init_locals = src_body->init_locals;
    dst_target->vararg = src_body->vararg;
    dst_target->optimized = 1;
    dst_target->needs_thread_attach = src_body->needs_thread_attach;
    dst_target->is_invoke = src_body->is_invoke;
    dst_target->is_verbose = src_body->is_verbose;
    __sync_synchronize();
    dst_target->transformed = 1;
}

IOS_BRIDGE_EXPORT void sts2_ios_interp_detour_method(void *src_mono_method, void *dst_mono_method) {
    if (!src_mono_method || !dst_mono_method) {
        return;
    }

    Sts2InterpMethod *src_imethod = mono_interp_get_imethod(src_mono_method);
    Sts2InterpMethod *dst_imethod = mono_interp_get_imethod(dst_mono_method);
    if (!src_imethod || !dst_imethod) {
        return;
    }

    // Mark both as optimized before transforming dst_mono_method so tiering opcodes
    // (MINT_TIER_ENTER_METHOD / MINT_TIER_PATCHPOINT) are omitted and never re-transform src_imethod.
    dst_imethod->optimized = 1;
    src_imethod->optimized = 1;

    if (!dst_imethod->transformed) {
        mono_compile_method(dst_mono_method);
    }

    Sts2InterpMethod *compiled_dst = dst_imethod->optimized_imethod ? dst_imethod->optimized_imethod : dst_imethod;
    if (!compiled_dst->transformed || !compiled_dst->code) {
        char *src_name = mono_method_full_name(src_mono_method, 1);
        char *dst_name = mono_method_full_name(dst_mono_method, 1);
        NSLog(@"[STS2Launcher iOS] Failed to transform detour target %s -> %s",
              src_name ? src_name : "<null>",
              dst_name ? dst_name : "<null>");
        if (src_name) free(src_name);
        if (dst_name) free(dst_name);
        return;
    }

    if (src_imethod->optimized_imethod) {
        sts2_copy_interp_body(src_imethod->optimized_imethod, compiled_dst);
    }
    sts2_copy_interp_body(src_imethod, compiled_dst);

    char *src_name = mono_method_full_name(src_mono_method, 1);
    char *dst_name = mono_method_full_name(dst_mono_method, 1);
    NSLog(@"[STS2Launcher iOS] Detoured interp method %s -> %s",
          src_name ? src_name : "<null>",
          dst_name ? dst_name : "<null>");
    if (src_name) free(src_name);
    if (dst_name) free(dst_name);
}

}
