using System;
using System.ComponentModel.DataAnnotations;

namespace MigrateDatabase.MssqlEntities.Arcadia;

public class MSSQLNPCResource
{
    [Key] public int id { get; set; }
    public int text_id { get; set; }
    public int name_text_id { get; set; }
    public int race_id { get; set; }
    public int sexsual_id { get; set; }
    public int x { get; set; }
    public int y { get; set; }
    public int z { get; set; }
    public int face { get; set; }
    public int local_flag { get; set; }
    public bool is_periodic { get; set; }
    public DateTime begin_of_period { get; set; }
    public DateTime end_of_period { get; set; }
    public int face_x { get; set; }
    public int face_y { get; set; }
    public int face_z { get; set; }
    public string model_file { get; set; }
    public int hair_id { get; set; }
    public int face_id { get; set; }
    public int body_id { get; set; }
    public int weapon_item_id { get; set; }
    public int shield_item_id { get; set; }
    public int clothes_item_id { get; set; }
    public int helm_item_id { get; set; }
    public int gloves_item_id { get; set; }
    public int boots_item_id { get; set; }
    public int belt_item_id { get; set; }
    public int mantle_item_id { get; set; }
    public int necklace_item_id { get; set; }
    public int earring_item_id { get; set; }
    public int ring1_item_id { get; set; }
    public int ring2_item_id { get; set; }
    public int motion_id { get; set; }
    public int is_roam { get; set; }
    public int roaming_id { get; set; }
    public int standard_walk_speed { get; set; }
    public int standard_run_speed { get; set; }
    public int walk_speed { get; set; }
    public int run_speed { get; set; }
    public int attackable { get; set; }
    public int offensive_type { get; set; }
    public int spawn_type { get; set; }
    public int chase_range { get; set; }
    public int regen_time { get; set; }
    public int level { get; set; }
    public int stat_id { get; set; }
    public int attack_range { get; set; }
    public int attack_speed_type { get; set; }
    public int hp { get; set; }
    public int mp { get; set; }
    public int attack_point { get; set; }
    public int magic_point { get; set; }
    public int defence { get; set; }
    public int magic_defence { get; set; }
    public int attack_speed { get; set; }
    public int magic_speed { get; set; }
    public int accuracy { get; set; }
    public int avoid { get; set; }
    public int magic_accuracy { get; set; }
    public int magic_avoid { get; set; }
    public string ai_script { get; set; }
    public string contact_script { get; set; }
    public int texture_group { get; set; }
    public int type { get; set; }
}
