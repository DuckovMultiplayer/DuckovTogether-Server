#pragma once

#ifdef DUCKOV_GUARD_EXPORTS
#define DUCKOV_API __declspec(dllexport)
#else
#define DUCKOV_API __declspec(dllimport)
#endif

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    uint32_t player_id;
    uint32_t sequence;
    uint64_t timestamp;
    uint32_t checksum;
    uint8_t signature[32];
} PacketHeader;

typedef struct {
    int32_t entity_id;
    float pos_x, pos_y, pos_z;
    float health;
    float damage;
    uint32_t action_type;
} GameAction;

typedef struct {
    uint32_t player_id;
    uint32_t violation_type;
    uint32_t severity;
    uint64_t timestamp;
    char details[256];
} ViolationReport;

typedef enum {
    VIOLATION_NONE = 0,
    VIOLATION_SPEED_HACK = 1,
    VIOLATION_DAMAGE_HACK = 2,
    VIOLATION_POSITION_HACK = 3,
    VIOLATION_HEALTH_HACK = 4,
    VIOLATION_SEQUENCE_HACK = 5,
    VIOLATION_SIGNATURE_INVALID = 6,
    VIOLATION_TIMESTAMP_INVALID = 7,
    VIOLATION_RATE_LIMIT = 8
} ViolationType;

DUCKOV_API int32_t dg_init(const char* server_key, uint32_t key_len);
DUCKOV_API void dg_shutdown();

DUCKOV_API int32_t dg_register_player(uint32_t player_id, const uint8_t* session_key, uint32_t key_len);
DUCKOV_API void dg_unregister_player(uint32_t player_id);

DUCKOV_API int32_t dg_validate_packet(uint32_t player_id, const uint8_t* data, uint32_t len, PacketHeader* out_header);
DUCKOV_API int32_t dg_sign_packet(uint32_t player_id, uint8_t* data, uint32_t len, PacketHeader* header);

DUCKOV_API int32_t dg_validate_action(uint32_t player_id, const GameAction* action, ViolationReport* report);
DUCKOV_API int32_t dg_validate_position(uint32_t player_id, float x, float y, float z, float delta_time);
DUCKOV_API int32_t dg_validate_damage(uint32_t player_id, int32_t target_id, float damage, float distance);
DUCKOV_API int32_t dg_validate_health(uint32_t player_id, float old_health, float new_health, float max_health);

DUCKOV_API void dg_update_player_position(uint32_t player_id, float x, float y, float z);
DUCKOV_API void dg_update_player_health(uint32_t player_id, float health);

DUCKOV_API uint32_t dg_get_violation_count(uint32_t player_id);
DUCKOV_API int32_t dg_get_last_violation(uint32_t player_id, ViolationReport* report);
DUCKOV_API void dg_clear_violations(uint32_t player_id);

DUCKOV_API uint32_t dg_compute_checksum(const uint8_t* data, uint32_t len);
DUCKOV_API void dg_encrypt_data(uint8_t* data, uint32_t len, const uint8_t* key, uint32_t key_len);
DUCKOV_API void dg_decrypt_data(uint8_t* data, uint32_t len, const uint8_t* key, uint32_t key_len);

DUCKOV_API int32_t dg_validate_weapon_fire(uint32_t player_id, int32_t weapon_id, float fire_rate);
DUCKOV_API int32_t dg_validate_item_pickup(uint32_t player_id, int32_t item_id, float distance);
DUCKOV_API void dg_set_player_state(uint32_t player_id, int32_t state_flags);
DUCKOV_API int32_t dg_should_ban_player(uint32_t player_id);
DUCKOV_API int32_t dg_validate_teleport(uint32_t player_id, float from_x, float from_y, float from_z,
                                         float to_x, float to_y, float to_z, int32_t is_allowed);
DUCKOV_API void dg_set_max_health(uint32_t player_id, float max_health);

#ifdef __cplusplus
}
#endif
