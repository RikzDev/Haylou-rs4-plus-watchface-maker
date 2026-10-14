/* Data-only import table. Kept out of clock.c so the compiler cannot
   turn these addresses back into PLT calls. */
extern char get_local_time[];
extern char apph_cfg_time_format_get[];
extern char sprintf[];
extern char dlmodule_get_user_data[];
extern char dlmodule_free_user_data[];
extern char lv_img_create[];
extern char lv_img_set_src[];
extern char lv_label_create[];
extern char lv_label_set_text[];
extern char lv_ext_set_local_font[];
extern char lv_obj_set_pos[];
extern char lv_page_glue_obj[];
extern char lv_obj_set_parent_event[];
extern char lv_task_create[];
extern char lv_task_del[];
extern char app_clock_register[];
extern char apph_todayinfo_target_info_get[];
extern char apph_hrspo2_info_get[];
extern char get_sync_data[];
extern char lv_img_set_angle[];
extern char lv_img_set_pivot[];
extern char lv_img_set_antialias[];
extern char rt_malloc[];
extern char rt_free[];
extern char dlmodule_set_res_mng[];
extern char dlmodule_get_res_mng[];
extern char lv_ext_res_mng_builtin_init[];
extern char lv_ext_res_mng_builtin_destroy[];
extern char lv_i18n_lang_pack[];
extern char lv_ext_get_locale[];
extern char lv_ext_set_locale[];
#if HAS_TEMPERATURE
extern char apph_cfg_weather_info_get[];
extern char get_local_time_from_utc0timestamps[];
extern char apph_cfg_unit_type_get[];
#endif

void *const fw[] = {
    get_local_time,
    apph_cfg_time_format_get,
    sprintf,
    dlmodule_get_user_data,
    dlmodule_free_user_data,
    lv_img_create,
    lv_img_set_src,
    lv_label_create,
    lv_label_set_text,
    lv_ext_set_local_font,
    lv_obj_set_pos,
    lv_page_glue_obj,
    lv_obj_set_parent_event,
    lv_task_create,
    lv_task_del,
    app_clock_register,
    apph_todayinfo_target_info_get,
    apph_hrspo2_info_get,
    get_sync_data,
    lv_img_set_angle,
    lv_img_set_pivot,
    lv_img_set_antialias,
    rt_malloc,
    rt_free,
    dlmodule_set_res_mng,
    dlmodule_get_res_mng,
    lv_ext_res_mng_builtin_init,
    lv_ext_res_mng_builtin_destroy,
    lv_i18n_lang_pack,
    lv_ext_get_locale,
    lv_ext_set_locale,
#if HAS_TEMPERATURE
    apph_cfg_weather_info_get,
    get_local_time_from_utc0timestamps,
    apph_cfg_unit_type_get,
#endif
};
