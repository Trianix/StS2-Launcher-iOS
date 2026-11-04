#import <Foundation/Foundation.h>

#ifdef __cplusplus
extern "C" {
#endif

// Exported C functions called from STS2Mobile via [DllImport("__Internal")]
bool ios_share_text_file(const char *path);
int64_t ios_get_usable_space_bytes(const char *path);
const char *ios_get_device_diagnostics(void);
const char *ios_get_version_name(void);
void ios_set_keep_screen_on(bool keepOn);
const char *ios_get_documents_dir(void);
const char *ios_get_user_data_dir(void);
void ios_get_safe_area_insets(float *top, float *bottom, float *left, float *right);
void ios_restart_app(void);
void ios_request_game_launch_and_restart(const char *pck_path, const char *source);
bool ios_get_or_create_device_key(uint8_t *key_out, int key_len);

#ifdef __cplusplus
}
#endif
