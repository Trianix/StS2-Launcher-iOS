#import <Foundation/Foundation.h>

#ifndef IOS_BRIDGE_EXPORT
#ifdef __cplusplus
#define IOS_BRIDGE_EXPORT extern "C" __attribute__((used, visibility("default")))
#else
#define IOS_BRIDGE_EXPORT __attribute__((used, visibility("default")))
#endif
#endif

#ifdef __cplusplus
extern "C" {
#endif

// Exported C functions called from STS2Mobile via [DllImport("__Internal")]
IOS_BRIDGE_EXPORT bool ios_share_text_file(const char *path);
IOS_BRIDGE_EXPORT int64_t ios_get_usable_space_bytes(const char *path);
IOS_BRIDGE_EXPORT const char *ios_get_device_diagnostics(void);
IOS_BRIDGE_EXPORT const char *ios_get_version_name(void);
IOS_BRIDGE_EXPORT void ios_set_keep_screen_on(bool keepOn);
IOS_BRIDGE_EXPORT const char *ios_get_documents_dir(void);
IOS_BRIDGE_EXPORT const char *ios_get_user_data_dir(void);
IOS_BRIDGE_EXPORT void ios_get_safe_area_insets(float *top, float *bottom, float *left, float *right);
IOS_BRIDGE_EXPORT void ios_restart_app(void);
IOS_BRIDGE_EXPORT void ios_request_game_launch_and_restart(const char *pck_path, const char *source);
IOS_BRIDGE_EXPORT bool ios_get_or_create_device_key(uint8_t *key_out, int key_len);
typedef int (*Sts2NativeCallbackFn)(int val);
IOS_BRIDGE_EXPORT int sts2_test_native_callback_roundtrip(Sts2NativeCallbackFn fn, int val);
// Versioned iOS interpreter detour contract: 1 means the detour body was copied;
// negative results identify a rejected operation and must fail the managed patch.
IOS_BRIDGE_EXPORT int32_t sts2_ios_interp_detour_method_v1(void *src_mono_method, void *dst_mono_method);

#ifdef __cplusplus
}
#endif
