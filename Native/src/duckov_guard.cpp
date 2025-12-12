#define DUCKOV_GUARD_EXPORTS
#include "../include/duckov_guard.h"

#include <unordered_map>
#include <vector>
#include <cstring>
#include <cmath>
#include <chrono>
#include <mutex>
#include <random>

namespace {

struct PlayerState {
    uint32_t player_id;
    uint8_t session_key[32];
    uint32_t key_len;
    
    float last_x, last_y, last_z;
    uint64_t last_position_time;
    float last_health;
    float max_health;
    
    uint32_t last_sequence;
    uint64_t last_packet_time;
    
    std::vector<ViolationReport> violations;
    uint32_t packet_count_per_second;
    uint64_t packet_window_start;
    
    float max_speed;
    float max_damage;
    uint32_t rate_limit;
    
    uint64_t last_fire_time;
    uint32_t fire_count_per_second;
    uint64_t fire_window_start;
    
    float position_history_x[10];
    float position_history_y[10];
    float position_history_z[10];
    uint64_t position_history_time[10];
    int position_history_index;
    
    uint32_t item_pickup_count;
    uint64_t item_pickup_window_start;
    
    bool is_sprinting;
    bool is_in_vehicle;
    bool is_dead;
    float accumulated_damage_dealt;
    float accumulated_damage_taken;
    uint64_t damage_window_start;
};

std::unordered_map<uint32_t, PlayerState> g_players;
std::mutex g_mutex;
uint8_t g_server_key[64];
uint32_t g_server_key_len = 0;
bool g_initialized = false;

const float DEFAULT_MAX_SPEED = 12.0f;
const float SPRINT_MAX_SPEED = 18.0f;
const float VEHICLE_MAX_SPEED = 50.0f;
const float DEFAULT_MAX_DAMAGE = 300.0f;
const float HEADSHOT_MULTIPLIER = 4.0f;
const uint32_t DEFAULT_RATE_LIMIT = 60;
const float POSITION_TOLERANCE = 0.3f;
const uint64_t TIMESTAMP_TOLERANCE_MS = 3000;
const float MAX_TELEPORT_DISTANCE = 50.0f;
const float MIN_FIRE_INTERVAL_MS = 50.0f;
const uint32_t MAX_VIOLATIONS_BEFORE_BAN = 10;
const float GRAVITY_CHECK_THRESHOLD = 2.0f;

uint64_t get_current_time_ms() {
    auto now = std::chrono::system_clock::now();
    return std::chrono::duration_cast<std::chrono::milliseconds>(now.time_since_epoch()).count();
}

uint32_t fnv1a_hash(const uint8_t* data, uint32_t len) {
    uint32_t hash = 2166136261u;
    for (uint32_t i = 0; i < len; i++) {
        hash ^= data[i];
        hash *= 16777619u;
    }
    return hash;
}

void xor_cipher(uint8_t* data, uint32_t len, const uint8_t* key, uint32_t key_len) {
    for (uint32_t i = 0; i < len; i++) {
        data[i] ^= key[i % key_len];
        data[i] = ((data[i] << 3) | (data[i] >> 5));
    }
}

void compute_signature(const uint8_t* data, uint32_t len, const uint8_t* key, uint32_t key_len, uint8_t* out_sig) {
    uint32_t h1 = fnv1a_hash(data, len);
    uint32_t h2 = fnv1a_hash(key, key_len);
    
    for (int i = 0; i < 32; i++) {
        uint32_t mix = h1 ^ (h2 << (i % 16)) ^ (i * 0x9E3779B9);
        out_sig[i] = (uint8_t)(mix ^ (mix >> 8) ^ (mix >> 16) ^ (mix >> 24));
        h1 = fnv1a_hash(out_sig, i + 1);
    }
}

bool verify_signature(const uint8_t* data, uint32_t len, const uint8_t* key, uint32_t key_len, const uint8_t* sig) {
    uint8_t computed[32];
    compute_signature(data, len, key, key_len, computed);
    return memcmp(computed, sig, 32) == 0;
}

void add_violation(PlayerState& player, ViolationType type, uint32_t severity, const char* details) {
    ViolationReport report;
    report.player_id = player.player_id;
    report.violation_type = type;
    report.severity = severity;
    report.timestamp = get_current_time_ms();
    strncpy(report.details, details, sizeof(report.details) - 1);
    report.details[sizeof(report.details) - 1] = '\0';
    player.violations.push_back(report);
}

}

extern "C" {

DUCKOV_API int32_t dg_init(const char* server_key, uint32_t key_len) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    if (g_initialized) return 0;
    
    if (key_len > 64) key_len = 64;
    memcpy(g_server_key, server_key, key_len);
    g_server_key_len = key_len;
    g_initialized = true;
    
    return 1;
}

DUCKOV_API void dg_shutdown() {
    std::lock_guard<std::mutex> lock(g_mutex);
    g_players.clear();
    g_initialized = false;
}

DUCKOV_API int32_t dg_register_player(uint32_t player_id, const uint8_t* session_key, uint32_t key_len) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    if (!g_initialized) return 0;
    
    PlayerState state = {};
    state.player_id = player_id;
    state.key_len = (key_len > 32) ? 32 : key_len;
    if (session_key && key_len > 0) {
        memcpy(state.session_key, session_key, state.key_len);
    } else {
        std::random_device rd;
        std::mt19937 gen(rd());
        for (int i = 0; i < 32; i++) {
            state.session_key[i] = (uint8_t)(gen() & 0xFF);
        }
        state.key_len = 32;
    }
    
    state.last_x = state.last_y = state.last_z = 0;
    state.last_position_time = get_current_time_ms();
    state.last_health = 100.0f;
    state.max_health = 100.0f;
    state.last_sequence = 0;
    state.last_packet_time = 0;
    state.packet_count_per_second = 0;
    state.packet_window_start = get_current_time_ms();
    state.max_speed = DEFAULT_MAX_SPEED;
    state.max_damage = DEFAULT_MAX_DAMAGE;
    state.rate_limit = DEFAULT_RATE_LIMIT;
    
    state.last_fire_time = 0;
    state.fire_count_per_second = 0;
    state.fire_window_start = get_current_time_ms();
    state.position_history_index = 0;
    memset(state.position_history_x, 0, sizeof(state.position_history_x));
    memset(state.position_history_y, 0, sizeof(state.position_history_y));
    memset(state.position_history_z, 0, sizeof(state.position_history_z));
    memset(state.position_history_time, 0, sizeof(state.position_history_time));
    state.item_pickup_count = 0;
    state.item_pickup_window_start = get_current_time_ms();
    state.is_sprinting = false;
    state.is_in_vehicle = false;
    state.is_dead = false;
    state.accumulated_damage_dealt = 0;
    state.accumulated_damage_taken = 0;
    state.damage_window_start = get_current_time_ms();
    
    g_players[player_id] = state;
    return 1;
}

DUCKOV_API void dg_unregister_player(uint32_t player_id) {
    std::lock_guard<std::mutex> lock(g_mutex);
    g_players.erase(player_id);
}

DUCKOV_API int32_t dg_validate_packet(uint32_t player_id, const uint8_t* data, uint32_t len, PacketHeader* out_header) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    auto it = g_players.find(player_id);
    if (it == g_players.end()) return 0;
    
    auto& player = it->second;
    uint64_t now = get_current_time_ms();
    
    if (now - player.packet_window_start > 1000) {
        player.packet_count_per_second = 0;
        player.packet_window_start = now;
    }
    
    player.packet_count_per_second++;
    if (player.packet_count_per_second > player.rate_limit) {
        add_violation(player, VIOLATION_RATE_LIMIT, 1, "Rate limit exceeded");
        return 0;
    }
    
    if (len < sizeof(PacketHeader)) return 0;
    
    memcpy(out_header, data, sizeof(PacketHeader));
    
    if (out_header->sequence <= player.last_sequence && player.last_sequence > 0) {
        add_violation(player, VIOLATION_SEQUENCE_HACK, 2, "Invalid sequence number");
        return 0;
    }
    
    int64_t time_diff = (int64_t)now - (int64_t)out_header->timestamp;
    if (time_diff < -TIMESTAMP_TOLERANCE_MS || time_diff > TIMESTAMP_TOLERANCE_MS) {
        add_violation(player, VIOLATION_TIMESTAMP_INVALID, 2, "Invalid timestamp");
        return 0;
    }
    
    uint32_t computed_checksum = fnv1a_hash(data + sizeof(PacketHeader), len - sizeof(PacketHeader));
    if (computed_checksum != out_header->checksum) {
        add_violation(player, VIOLATION_SIGNATURE_INVALID, 3, "Checksum mismatch");
        return 0;
    }
    
    if (!verify_signature(data + sizeof(PacketHeader), len - sizeof(PacketHeader), 
                         player.session_key, player.key_len, out_header->signature)) {
        add_violation(player, VIOLATION_SIGNATURE_INVALID, 3, "Signature verification failed");
        return 0;
    }
    
    player.last_sequence = out_header->sequence;
    player.last_packet_time = now;
    
    return 1;
}

DUCKOV_API int32_t dg_sign_packet(uint32_t player_id, uint8_t* data, uint32_t len, PacketHeader* header) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    auto it = g_players.find(player_id);
    if (it == g_players.end()) return 0;
    
    auto& player = it->second;
    
    header->player_id = player_id;
    header->sequence = ++player.last_sequence;
    header->timestamp = get_current_time_ms();
    header->checksum = fnv1a_hash(data, len);
    compute_signature(data, len, player.session_key, player.key_len, header->signature);
    
    return 1;
}

DUCKOV_API int32_t dg_validate_position(uint32_t player_id, float x, float y, float z, float delta_time) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    auto it = g_players.find(player_id);
    if (it == g_players.end()) return 0;
    
    auto& player = it->second;
    
    if (delta_time <= 0) delta_time = 0.016f;
    
    float dx = x - player.last_x;
    float dy = y - player.last_y;
    float dz = z - player.last_z;
    float distance = std::sqrt(dx*dx + dy*dy + dz*dz);
    float speed = distance / delta_time;
    
    float max_allowed = player.max_speed * (1.0f + POSITION_TOLERANCE);
    
    if (speed > max_allowed && distance > 1.0f) {
        char details[256];
        snprintf(details, sizeof(details), "Speed: %.2f, Max: %.2f, Dist: %.2f", speed, max_allowed, distance);
        add_violation(player, VIOLATION_SPEED_HACK, 2, details);
        return 0;
    }
    
    player.last_x = x;
    player.last_y = y;
    player.last_z = z;
    player.last_position_time = get_current_time_ms();
    
    return 1;
}

DUCKOV_API int32_t dg_validate_damage(uint32_t player_id, int32_t target_id, float damage, float distance) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    auto it = g_players.find(player_id);
    if (it == g_players.end()) return 0;
    
    auto& player = it->second;
    
    if (damage < 0 || damage > player.max_damage) {
        char details[256];
        snprintf(details, sizeof(details), "Damage: %.2f, Max: %.2f", damage, player.max_damage);
        add_violation(player, VIOLATION_DAMAGE_HACK, 3, details);
        return 0;
    }
    
    const float MAX_ATTACK_RANGE = 100.0f;
    if (distance > MAX_ATTACK_RANGE) {
        char details[256];
        snprintf(details, sizeof(details), "Attack distance: %.2f", distance);
        add_violation(player, VIOLATION_POSITION_HACK, 2, details);
        return 0;
    }
    
    return 1;
}

DUCKOV_API int32_t dg_validate_health(uint32_t player_id, float old_health, float new_health, float max_health) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    auto it = g_players.find(player_id);
    if (it == g_players.end()) return 0;
    
    auto& player = it->second;
    
    if (new_health > max_health + 0.1f) {
        char details[256];
        snprintf(details, sizeof(details), "Health: %.2f, Max: %.2f", new_health, max_health);
        add_violation(player, VIOLATION_HEALTH_HACK, 3, details);
        return 0;
    }
    
    if (new_health > old_health + 50.0f && old_health > 0) {
        char details[256];
        snprintf(details, sizeof(details), "Health jump: %.2f -> %.2f", old_health, new_health);
        add_violation(player, VIOLATION_HEALTH_HACK, 2, details);
        return 0;
    }
    
    player.last_health = new_health;
    return 1;
}

DUCKOV_API int32_t dg_validate_action(uint32_t player_id, const GameAction* action, ViolationReport* report) {
    if (!action) return 0;
    
    int valid = 1;
    
    valid &= dg_validate_position(player_id, action->pos_x, action->pos_y, action->pos_z, 0.016f);
    
    if (action->damage > 0) {
        float dist = std::sqrt(action->pos_x * action->pos_x + action->pos_z * action->pos_z);
        valid &= dg_validate_damage(player_id, action->entity_id, action->damage, dist);
    }
    
    if (!valid && report) {
        dg_get_last_violation(player_id, report);
    }
    
    return valid;
}

DUCKOV_API void dg_update_player_position(uint32_t player_id, float x, float y, float z) {
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_players.find(player_id);
    if (it != g_players.end()) {
        it->second.last_x = x;
        it->second.last_y = y;
        it->second.last_z = z;
        it->second.last_position_time = get_current_time_ms();
    }
}

DUCKOV_API void dg_update_player_health(uint32_t player_id, float health) {
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_players.find(player_id);
    if (it != g_players.end()) {
        it->second.last_health = health;
    }
}

DUCKOV_API uint32_t dg_get_violation_count(uint32_t player_id) {
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_players.find(player_id);
    if (it == g_players.end()) return 0;
    return (uint32_t)it->second.violations.size();
}

DUCKOV_API int32_t dg_get_last_violation(uint32_t player_id, ViolationReport* report) {
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_players.find(player_id);
    if (it == g_players.end() || it->second.violations.empty()) return 0;
    
    *report = it->second.violations.back();
    return 1;
}

DUCKOV_API void dg_clear_violations(uint32_t player_id) {
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_players.find(player_id);
    if (it != g_players.end()) {
        it->second.violations.clear();
    }
}

DUCKOV_API uint32_t dg_compute_checksum(const uint8_t* data, uint32_t len) {
    return fnv1a_hash(data, len);
}

DUCKOV_API void dg_encrypt_data(uint8_t* data, uint32_t len, const uint8_t* key, uint32_t key_len) {
    xor_cipher(data, len, key, key_len);
}

DUCKOV_API void dg_decrypt_data(uint8_t* data, uint32_t len, const uint8_t* key, uint32_t key_len) {
    for (uint32_t i = 0; i < len; i++) {
        data[i] = ((data[i] >> 3) | (data[i] << 5));
        data[i] ^= key[i % key_len];
    }
}

DUCKOV_API int32_t dg_validate_weapon_fire(uint32_t player_id, int32_t weapon_id, float fire_rate) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    auto it = g_players.find(player_id);
    if (it == g_players.end()) return 0;
    
    auto& player = it->second;
    uint64_t now = get_current_time_ms();
    
    if (now - player.fire_window_start > 1000) {
        player.fire_count_per_second = 0;
        player.fire_window_start = now;
    }
    
    float min_interval = (fire_rate > 0) ? (1000.0f / fire_rate) : MIN_FIRE_INTERVAL_MS;
    uint64_t time_since_last = now - player.last_fire_time;
    
    if (time_since_last < (uint64_t)(min_interval * 0.8f) && player.last_fire_time > 0) {
        char details[256];
        snprintf(details, sizeof(details), "Fire rate hack: interval=%llums, min=%0.fms", 
                 (unsigned long long)time_since_last, min_interval);
        add_violation(player, VIOLATION_RATE_LIMIT, 2, details);
        return 0;
    }
    
    player.fire_count_per_second++;
    player.last_fire_time = now;
    
    return 1;
}

DUCKOV_API int32_t dg_validate_item_pickup(uint32_t player_id, int32_t item_id, float distance) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    auto it = g_players.find(player_id);
    if (it == g_players.end()) return 0;
    
    auto& player = it->second;
    uint64_t now = get_current_time_ms();
    
    const float MAX_PICKUP_DISTANCE = 5.0f;
    if (distance > MAX_PICKUP_DISTANCE) {
        char details[256];
        snprintf(details, sizeof(details), "Item pickup distance: %.2f (max: %.2f)", distance, MAX_PICKUP_DISTANCE);
        add_violation(player, VIOLATION_POSITION_HACK, 2, details);
        return 0;
    }
    
    if (now - player.item_pickup_window_start > 1000) {
        player.item_pickup_count = 0;
        player.item_pickup_window_start = now;
    }
    
    const uint32_t MAX_PICKUPS_PER_SECOND = 10;
    player.item_pickup_count++;
    if (player.item_pickup_count > MAX_PICKUPS_PER_SECOND) {
        add_violation(player, VIOLATION_RATE_LIMIT, 1, "Item pickup rate exceeded");
        return 0;
    }
    
    return 1;
}

DUCKOV_API void dg_set_player_state(uint32_t player_id, int32_t state_flags) {
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_players.find(player_id);
    if (it != g_players.end()) {
        it->second.is_sprinting = (state_flags & 1) != 0;
        it->second.is_in_vehicle = (state_flags & 2) != 0;
        it->second.is_dead = (state_flags & 4) != 0;
        
        if (it->second.is_sprinting) {
            it->second.max_speed = SPRINT_MAX_SPEED;
        } else if (it->second.is_in_vehicle) {
            it->second.max_speed = VEHICLE_MAX_SPEED;
        } else {
            it->second.max_speed = DEFAULT_MAX_SPEED;
        }
    }
}

DUCKOV_API int32_t dg_should_ban_player(uint32_t player_id) {
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_players.find(player_id);
    if (it == g_players.end()) return 0;
    
    uint32_t severe_count = 0;
    for (const auto& v : it->second.violations) {
        if (v.severity >= 3) severe_count++;
    }
    
    return (it->second.violations.size() >= MAX_VIOLATIONS_BEFORE_BAN || severe_count >= 3) ? 1 : 0;
}

DUCKOV_API int32_t dg_validate_teleport(uint32_t player_id, float from_x, float from_y, float from_z,
                                         float to_x, float to_y, float to_z, int32_t is_allowed) {
    if (is_allowed) return 1;
    
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_players.find(player_id);
    if (it == g_players.end()) return 0;
    
    float dx = to_x - from_x;
    float dy = to_y - from_y;
    float dz = to_z - from_z;
    float dist = std::sqrt(dx*dx + dy*dy + dz*dz);
    
    if (dist > MAX_TELEPORT_DISTANCE) {
        char details[256];
        snprintf(details, sizeof(details), "Teleport: %.2f units", dist);
        add_violation(it->second, VIOLATION_POSITION_HACK, 3, details);
        return 0;
    }
    
    return 1;
}

DUCKOV_API void dg_set_max_health(uint32_t player_id, float max_health) {
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_players.find(player_id);
    if (it != g_players.end()) {
        it->second.max_health = max_health;
    }
}

}
