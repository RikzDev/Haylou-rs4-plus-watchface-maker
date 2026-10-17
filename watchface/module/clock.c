/* LS11 face: background plus live labels. The build passes the layout
   with gcc -include, so the id in this module matches the dial package.
   Data calls match dial_284 (New Ammeter). */

typedef struct {
    unsigned short year;
    unsigned char month;
    unsigned char day;
    unsigned char hour;
    unsigned char min;
    unsigned char sec;
    unsigned char wday;
} clock_time_t;

typedef struct {
    unsigned raw0;
    unsigned calories_milli;
    unsigned distance_raw;
    unsigned steps;
    /* Only this prefix has been decoded. Official dial_284 gives the
       firmware a larger output area (sp+64 in a 92-byte frame). A 16-byte
       destination at the end of our frame lets a larger firmware write
       overwrite saved registers. Keep opaque output space after it. */
    unsigned reserved[12];
} today_t;

typedef struct {
    unsigned char flags;
    unsigned char bpm;
    /* The getter returns HR/SpO2 data, not just the two bytes we display. */
    unsigned char reserved[6];
} hr_t;

/* Uploader rejects earlier editor modules with undersized output buffers. */
const unsigned haylou_editor_abi_version = 2;

typedef struct {
    unsigned header;
    unsigned data_size;
    const unsigned char *data;
} img_dsc_t;

#ifndef ANIMATION_COUNT
#define ANIMATION_COUNT 0
#endif
#ifndef ANIMATION_EXTERN
#define ANIMATION_EXTERN
#endif
#ifndef ANIMATION_DEFS
#define ANIMATION_DEFS
#endif
#ifndef LABEL_COUNT
#define LABEL_COUNT 0
#endif

typedef struct {
    int x;
    int y;
    int interval;
    int count;
    int source;
    int min_value;
    int max_value;
    const img_dsc_t *const *frames;
    const int *offsets;
} animation_def_t;

typedef struct {
    void *image;
    unsigned frame;
} animation_state_t;

#if ANIMATION_COUNT
ANIMATION_EXTERN
ANIMATION_DEFS
#endif

#ifndef GLYPH_LINE_COUNT
#define GLYPH_LINE_COUNT 0
#endif
#ifndef GLYPH_SLOT_COUNT
#define GLYPH_SLOT_COUNT 0
#endif
#ifndef HAS_TEMPERATURE
#define HAS_TEMPERATURE 0
#endif

typedef struct {
    void *background;
    void *hour_hand;
    void *minute_hand;
    void *second_hand;
    /* The face state lives in the module's small per-dial user-data area.
       Do not reserve 32 pointers for text labels when this build uses the
       image-glyph path (LABEL_COUNT == 0).  Large imported TBWF faces can
       otherwise exhaust that area before their first frame is drawn. */
    void *labels[LABEL_COUNT > 0 ? LABEL_COUNT : 1];
    void *glyphs[GLYPH_SLOT_COUNT > 0 ? GLYPH_SLOT_COUNT : 1];
    animation_state_t animations[ANIMATION_COUNT > 0 ? ANIMATION_COUNT : 1];
    unsigned animation_ticks;
    void *task;
#if HAS_TEMPERATURE || HAS_WEATHER
    unsigned weather_key;
    int temperature_c;
    int temperature_valid;
#if HAS_WEATHER
    int weather_condition;
#endif
#endif
} face_t;

#ifndef HAS_HOUR
#define HAS_HOUR 0
#endif
#ifndef HAS_MINUTE
#define HAS_MINUTE 0
#endif
#ifndef HAS_SECOND
#define HAS_SECOND 0
#endif

#if HAS_HOUR
extern img_dsc_t hour_hand;
#endif
#if HAS_MINUTE
extern img_dsc_t minute_hand;
#endif
#if HAS_SECOND
extern img_dsc_t second_hand;
#endif

extern img_dsc_t vscode_bg;
extern void *const fw[];

#define FW_GET_TIME 0
#define FW_FORMAT 1
#define FW_SPRINTF 2
#define FW_GET_DATA 3
#define FW_FREE_DATA 4
#define FW_IMG_CREATE 5
#define FW_IMG_SRC 6
#define FW_LABEL_CREATE 7
#define FW_LABEL_TEXT 8
#define FW_FONT 9
#define FW_POS 10
#define FW_GLUE 11
#define FW_PARENT 12
#define FW_TASK_CREATE 13
#define FW_TASK_DEL 14
#define FW_REGISTER 15
#define FW_TODAY 16
#define FW_HR 17
#define FW_SYNC 18
#define FW_ANGLE 19
#define FW_PIVOT 20
#define FW_ANTIALIAS 21
#define FW_MALLOC 22
#define FW_FREE 23
#define FW_SET_RES_MNG 24
#define FW_GET_RES_MNG 25
#define FW_RES_MNG_INIT 26
#define FW_RES_MNG_DESTROY 27
#define FW_LANG_PACK 28
#define FW_GET_LOCALE 29
#define FW_SET_LOCALE 30
#define FW_WEATHER 31
#define FW_UTC_LOCAL 32
#define FW_UNIT 33
#define FW_HIDDEN 34

/* Official dial_54: today +4 is milli-kcal, +8 is distance in 1/100000
   km, and +12 is steps. Bit 31 is metadata, not part of each value. */
static unsigned calories_value(unsigned raw)
{
    raw &= 0x7fffffffu;
    return raw <= 99999999u ? raw / 1000u : 99999u;
}

static const char *distance_unit(void)
{
#if HAS_DISTANCE
    if (fw[FW_UNIT] && ((int (*)(void))fw[FW_UNIT])() == 2)
        return "mi";
#endif
    return "km";
}

static unsigned distance_centi(unsigned raw)
{
#if HAS_DISTANCE
    raw &= 0x7fffffffu;
    if (distance_unit()[0] == 'm') {
        /* Same 0.6214 conversion as dial_54, without overflowing its
           32-bit multiply or importing a 64-bit division helper. */
        unsigned whole = (raw / 100000u) * 6214u;
        return whole / 100u + ((whole % 100u) * 100000u
            + (raw % 100000u) * 6214u) / 10000000u;
    }
    return raw / 1000u;
#else
    (void)raw;
    return 0;
#endif
}

static void turn_hands(face_t *face, const clock_time_t *now);

#if HAS_TEMPERATURE || HAS_WEATHER
static void update_weather(face_t *face, const clock_time_t *now)
{
    /* Official dials 77/100 leave 340 bytes for seven 48-byte records.
       Use a larger heap destination; do not add it to the per-dial state
       or expose saved stack registers to an incompletely decoded getter. */
    unsigned key = ((unsigned)now->year << 16) | ((unsigned)now->month << 12)
        | ((unsigned)now->day << 7) | now->hour;
    unsigned char *weather;
    if (face->weather_key == key)
        return;
    face->temperature_valid = 0;
#if HAS_WEATHER
    face->weather_condition = 0;
#endif
    if (!fw[FW_WEATHER] || !fw[FW_UTC_LOCAL] || !fw[FW_MALLOC] || !fw[FW_FREE])
        return;
    weather = ((void *(*)(unsigned))fw[FW_MALLOC])(384);
    if (!weather)
        return;
    for (int index = 0; index < 96; index++)
        ((volatile unsigned *)weather)[index] = 0;
    ((void (*)(void *, int))fw[FW_WEATHER])(weather, 0);
    face->weather_key = key;
    for (int index = 0; index < 7; index++) {
        const unsigned char *record = weather + index * 48;
        unsigned timestamp = (unsigned)record[12] | ((unsigned)record[13] << 8)
            | ((unsigned)record[14] << 16) | ((unsigned)record[15] << 24);
        unsigned local_buffer[8];
        const clock_time_t *date = (const clock_time_t *)local_buffer;
        if (!timestamp)
            continue;
        for (int word = 0; word < 8; word++)
            ((volatile unsigned *)local_buffer)[word] = 0;
        ((void (*)(void *, unsigned))fw[FW_UTC_LOCAL])(local_buffer, timestamp);
        if (date->year == now->year && date->month == now->month && date->day == now->day) {
            face->temperature_c = (short)((unsigned)record[8] | ((unsigned)record[9] << 8));
            face->temperature_valid = 1;
#if HAS_WEATHER
            face->weather_condition = record[1];
#endif
            break;
        }
    }
    ((void (*)(void *))fw[FW_FREE])(weather);
}

#endif

#if HAS_TEMPERATURE
static int temperature_value(const face_t *face)
{
    int value = face->temperature_c;
    if (fw[FW_UNIT] && ((int (*)(void))fw[FW_UNIT])() == 2)
        value = (value * 9) / 5 + 32;
    /* Four cells fit a sign plus three digits, or four positive digits. */
    return value < -999 ? -999 : value > 9999 ? 9999 : value;
}
#endif

#if HAS_WEATHER
static int weather_frame_index(const face_t *face, const clock_time_t *now)
{
    /* Official dial_77: condition byte +1, codes 1..12. Its day branch
       is inclusive 06:00..18:59; other hours use three night variants. */
    int condition = face->weather_condition;
    if (condition < 1 || condition > 12)
        return 0;
    if (now->hour < 6 || now->hour > 18) {
        if (condition == 1) return 13;
        if (condition == 2) return 14;
        if (condition == 4) return 15;
    }
    return condition;
}
#endif

#if GLYPH_LINE_COUNT
GLYPH_EXTERNS

static const img_dsc_t *glyph_for(int set, char ch)
{
    GLYPH_LOOKUP
}

static void write_u(char *dst, int slots, unsigned value, int zeros)
{
    char rev[8];
    int count = 0;
    int index;
    if (value == 0)
        rev[count++] = '0';
    while (value && count < 8) {
        rev[count++] = (char)('0' + (value % 10u));
        value /= 10u;
    }
    if (zeros) {
        while (count < slots && count < 8)
            rev[count++] = '0';
    }
    for (index = 0; index < slots; index++)
        dst[index] = ' ';
    if (count > slots)
        count = slots;
    for (index = 0; index < count; index++)
        dst[slots - 1 - index] = rev[index];
    dst[slots] = 0;
}

static void copy_text(char *dst, int slots, const char *text)
{
    int index;
    for (index = 0; index < slots; index++) {
        char ch = *text;
        dst[index] = ch ? ch : ' ';
        if (ch)
            text++;
    }
    dst[slots] = 0;
}

#if HAS_TEMPERATURE
static void write_i(char *dst, int slots, int value)
{
    write_u(dst, slots, (unsigned)(value < 0 ? -value : value), 0);
    if (value < 0) {
        int index = 0;
        while (index < slots && dst[index] == ' ')
            index++;
        if (index > 0)
            dst[index - 1] = '-';
    }
}
#endif

static void paint_glyphs(face_t *face, int line, const char *text)
{
    int origin = glyph_lines[line].slot;
    int index;
    for (index = 0; index < glyph_lines[line].slots; index++) {
        void *img = face->glyphs[origin + index];
        const img_dsc_t *src = glyph_for(glyph_lines[line].set, text[index] ? text[index] : ' ');
        if (img && src && fw[FW_IMG_SRC])
            ((void (*)(void *, const void *))fw[FW_IMG_SRC])(img, src);
    }
}

static void update_glyphs(face_t *face, const clock_time_t *now, unsigned steps, unsigned scaled, int heart, int battery,
    unsigned calories, unsigned distance, int ampm)
{
    /* Fixed custom-font text accepts up to 12 characters plus NUL. */
    char text[13];
    int line;
    for (line = 0; line < GLYPH_LINE_COUNT; line++) {
        switch (glyph_lines[line].source) {
        case SRC_HOUR:
            write_u(text, glyph_lines[line].slots, now->hour, 1);
            break;
        case SRC_MINUTE:
            write_u(text, glyph_lines[line].slots, now->min, 1);
            break;
        case SRC_SECOND:
            write_u(text, glyph_lines[line].slots, now->sec, 1);
            break;
        case SRC_MONTH:
            write_u(text, glyph_lines[line].slots, now->month, 1);
            break;
        case SRC_DAY:
            write_u(text, glyph_lines[line].slots, now->day, 1);
            break;
        case SRC_YEAR:
            write_u(text, glyph_lines[line].slots, now->year, 1);
            break;
        case SRC_WDAY:
            write_u(text, glyph_lines[line].slots, now->wday, 0);
            break;
        case SRC_STEPS:
            write_u(text, glyph_lines[line].slots, steps, 0);
            break;
        case SRC_SCALED:
            write_u(text, glyph_lines[line].slots, scaled, 0);
            break;
        case SRC_CALORIES:
            write_u(text, glyph_lines[line].slots, calories, 0);
            break;
        case SRC_DISTANCE: {
            int slots = glyph_lines[line].slots;
            write_u(text, slots - 3, distance / 100u, 0);
            text[slots - 3] = '.';
            text[slots - 2] = (char)('0' + (distance / 10u) % 10u);
            text[slots - 1] = (char)('0' + distance % 10u);
            text[slots] = 0;
            break;
        }
        case SRC_DISTANCE_INT:
            write_u(text, glyph_lines[line].slots, distance / 100u, 0);
            break;
        case SRC_DISTANCE_UNIT:
            copy_text(text, glyph_lines[line].slots, distance_unit());
            break;
        case SRC_AMPM:
            copy_text(text, glyph_lines[line].slots, ampm < 0 ? "" : ampm ? "PM" : "AM");
            break;
        case SRC_HEART:
            copy_text(text, glyph_lines[line].slots, heart < 0 ? "--" : "");
            if (heart >= 0)
                write_u(text, glyph_lines[line].slots, (unsigned)heart, 0);
            break;
        case SRC_BATTERY:
            if (battery < 0)
                copy_text(text, glyph_lines[line].slots, "--");
            else
                write_u(text, glyph_lines[line].slots, (unsigned)battery, 0);
            break;
#if HAS_TEMPERATURE
        case SRC_TEMPERATURE:
            if (face->temperature_valid)
                write_i(text, glyph_lines[line].slots, temperature_value(face));
            else
                copy_text(text, glyph_lines[line].slots, "--");
            break;
#endif
        default:
            copy_text(text, glyph_lines[line].slots, glyph_lines[line].text);
            break;
        }
        paint_glyphs(face, line, text);
    }
}
#endif

static void *place_still(void *parent, const img_dsc_t *src, int x, int y)
{
    void *img;
    if (!fw[FW_IMG_CREATE] || !fw[FW_IMG_SRC] || !fw[FW_POS])
        return 0;
    img = ((void *(*)(void *, const void *))fw[FW_IMG_CREATE])(parent, 0);
    if (!img)
        return 0;
    ((void (*)(void *, const void *))fw[FW_IMG_SRC])(img, src);
    ((void (*)(void *, int, int))fw[FW_POS])(img, x, y);
    if (fw[FW_GLUE])
        ((void (*)(void *, int))fw[FW_GLUE])(img, 1);
    if (fw[FW_PARENT])
        ((void (*)(void *, int))fw[FW_PARENT])(img, 1);
    return img;
}

#if ANIMATION_COUNT
static unsigned progress_frame(unsigned value, const animation_def_t *def)
{
    if (value <= (unsigned)def->min_value || def->max_value <= def->min_value)
        return 0;
    if (value >= (unsigned)def->max_value)
        return (unsigned)def->count - 1u;
    /* Range validation bounds the product to 99999 * 64, even when the
       firmware reading itself is larger than the configured maximum. */
    return ((value - (unsigned)def->min_value) * (unsigned)def->count)
        / (unsigned)(def->max_value - def->min_value + 1);
}
#endif

static void update_animations(face_t *face, const clock_time_t *now, unsigned steps, unsigned scaled, int heart, int battery,
    unsigned calories, unsigned distance, int ampm, int weather)
{
#if ANIMATION_COUNT
    int index;
    for (index = 0; index < ANIMATION_COUNT; index++) {
        const animation_def_t *def = &animation_defs[index];
        unsigned frame = 0;
        unsigned period;
        if (def->count <= 0 || !face->animations[index].image)
            continue;
#if HAS_AMPM_ANIMATION
        if (def->source == 14 && fw[FW_HIDDEN])
            ((void (*)(void *, int))fw[FW_HIDDEN])(face->animations[index].image, ampm < 0);
#endif
        if (def->source == 1) {
            if (battery >= 0)
                frame = ((unsigned)battery * (unsigned)def->count) / 101u;
        } else if (def->source == 2) {
            frame = (unsigned)now->wday % (unsigned)def->count;
        } else if (def->source == 3) {
            frame = (unsigned)(now->day - 1u) % (unsigned)def->count;
        } else if (def->source == 4) {
            frame = (unsigned)(now->month - 1u) % (unsigned)def->count;
        } else if (def->source == 5) {
            frame = (unsigned)now->sec % (unsigned)def->count;
        } else if (def->source == 6) {
            frame = (unsigned)now->min % (unsigned)def->count;
        } else if (def->source == 7) {
            frame = (unsigned)(now->hour % 12u) % (unsigned)def->count;
        } else if (def->source == 8) {
            frame = progress_frame(steps, def);
        } else if (def->source == 9) {
            frame = heart >= 0 ? progress_frame((unsigned)heart, def) : 0;
        } else if (def->source == 10) {
            frame = scaled % (unsigned)def->count;
        } else if (def->source == 11) {
            frame = 0;
        } else if (def->source == 12) {
            frame = progress_frame(calories, def);
        } else if (def->source == 13) {
            frame = progress_frame(distance / 100u, def);
        } else if (def->source == 14) {
            frame = ampm > 0 ? 1u : 0u;
        } else if (def->source == 15) {
            frame = (unsigned)weather;
        } else {
            period = (unsigned)(def->interval + 199) / 200u;
            if (period == 0)
                period = 1;
            frame = (face->animation_ticks / period) % (unsigned)def->count;
        }
        if (frame >= (unsigned)def->count)
            frame = (unsigned)def->count - 1u;
        if (face->animations[index].frame != frame && fw[FW_IMG_SRC]) {
            ((void (*)(void *, const void *))fw[FW_IMG_SRC])(
                face->animations[index].image,
                def->frames[frame]);
            if (fw[FW_POS])
                ((void (*)(void *, int, int))fw[FW_POS])(
                    face->animations[index].image,
                    def->x + def->offsets[frame * 2u],
                    def->y + def->offsets[frame * 2u + 1u]);
            face->animations[index].frame = frame;
        }
    }
#else
    (void)face;
    (void)now;
    (void)steps;
    (void)scaled;
    (void)heart;
    (void)battery;
    (void)calories;
    (void)distance;
    (void)ampm;
    (void)weather;
#endif
}

static void set_text(void *label, const char *text)
{
    if (label && fw[FW_LABEL_TEXT])
        ((void (*)(void *, const char *))fw[FW_LABEL_TEXT])(label, text);
}

static void *make_label(void *parent, int x, int y, unsigned color, int size)
{
    void *label;
    if (!fw[FW_LABEL_CREATE] || !fw[FW_FONT] || !fw[FW_POS] || !fw[FW_GLUE] || !fw[FW_PARENT])
        return 0;
    label = ((void *(*)(void *, const void *))fw[FW_LABEL_CREATE])(parent, 0);
    if (!label)
        return 0;
    /* This firmware only draws font size 1. Any other size faults the UI
       and reboots into the same face. Preview size stays in the editor. */
    (void)size;
    ((void (*)(void *, int, unsigned))fw[FW_FONT])(label, 1, color);
    ((void (*)(void *, int, int))fw[FW_POS])(label, x, y);
    ((void (*)(void *, int))fw[FW_GLUE])(label, 1);
    ((void (*)(void *, int))fw[FW_PARENT])(label, 1);
    return label;
}

static void tick(void *unused)
{
    face_t *face;
    clock_time_t now;
    today_t today;
    hr_t hr;
    char text[16];
    unsigned steps;
    unsigned calories, distance;
    int ampm, twelve_hour;
    int weather = 0;
    unsigned char pct;
    void *sync;
    (void)unused;
    /* Do not use aggregate zero initialization here. GCC turns it into a
       direct memset@plt call, which produces an unsupported JUMP_SLOT
       relocation in LS11 dial modules. */
    today.raw0 = 0;
    today.calories_milli = 0;
    today.distance_raw = 0;
    today.steps = 0;
    hr.flags = 0;
    hr.bpm = 0;
    if (!fw[FW_GET_DATA] || !fw[FW_GET_TIME])
        return;
    face = ((void *(*)(const char *, int))fw[FW_GET_DATA])(FACE, sizeof(face_t));
    if (!face)
        return;
    ((void (*)(clock_time_t *))fw[FW_GET_TIME])(&now);
#if HAS_TEMPERATURE || HAS_WEATHER
    update_weather(face, &now);
#endif
#if HAS_WEATHER
    weather = weather_frame_index(face, &now);
#endif
    turn_hands(face, &now);
    twelve_hour = fw[FW_FORMAT] && ((int (*)(void))fw[FW_FORMAT])() == 2;
    ampm = twelve_hour ? (now.hour >= 12 ? 1 : 0) : -1;
    if (twelve_hour) {
        if (now.hour == 0 || now.hour == 12)
            now.hour = 12;
        else if (now.hour > 12)
            now.hour = (unsigned char)(now.hour - 12);
    }
    if (fw[FW_TODAY])
        ((void (*)(today_t *))fw[FW_TODAY])(&today);
    if (fw[FW_HR])
        ((void (*)(hr_t *))fw[FW_HR])(&hr);
    sync = fw[FW_SYNC] ? ((void *(*)(int))fw[FW_SYNC])(2) : 0;
    steps = today.steps & 0x7fffffffu;
    if (steps > 99999u)
        steps = 99999u;
    calories = calories_value(today.calories_milli);
    distance = distance_centi(today.distance_raw);
    pct = sync ? ((unsigned char *)sync)[0x29] : 0;
    face->animation_ticks++;
    update_animations(
        face,
        &now,
        steps,
        (today.calories_milli & 0x7fffffffu) / 1000u,
        ((hr.flags & 1u) && hr.bpm) ? (int)hr.bpm : -1,
        sync ? (int)pct : -1, calories, distance, ampm, weather);
#if GLYPH_LINE_COUNT
    update_glyphs(
        face,
        &now,
        steps,
        (today.calories_milli & 0x7fffffffu) / 1000u,
        ((hr.flags & 1u) && hr.bpm) ? (int)hr.bpm : -1,
        sync ? (int)pct : -1, calories, distance, ampm);
#endif
    if (LABEL_COUNT <= 0 || !fw[FW_SPRINTF] || !fw[FW_LABEL_TEXT])
        return;
    for (int index = 0; index < LABEL_COUNT; index++) {
        switch (label_defs[index].source) {
        case SRC_HOUR:
            ((int (*)(char *, const char *, ...))fw[FW_SPRINTF])(text, "%02d", now.hour);
            set_text(face->labels[index], text);
            break;
        case SRC_MINUTE:
            ((int (*)(char *, const char *, ...))fw[FW_SPRINTF])(text, "%02d", now.min);
            set_text(face->labels[index], text);
            break;
        case SRC_SECOND:
            ((int (*)(char *, const char *, ...))fw[FW_SPRINTF])(text, "%02d", now.sec);
            set_text(face->labels[index], text);
            break;
        case SRC_MONTH:
            ((int (*)(char *, const char *, ...))fw[FW_SPRINTF])(text, "%02d", now.month);
            set_text(face->labels[index], text);
            break;
        case SRC_DAY:
            ((int (*)(char *, const char *, ...))fw[FW_SPRINTF])(text, "%02d", now.day);
            set_text(face->labels[index], text);
            break;
        case SRC_YEAR:
            ((int (*)(char *, const char *, ...))fw[FW_SPRINTF])(text, "%04u", (unsigned)now.year);
            set_text(face->labels[index], text);
            break;
        case SRC_WDAY:
            ((int (*)(char *, const char *, ...))fw[FW_SPRINTF])(text, "%u", (unsigned)now.wday);
            set_text(face->labels[index], text);
            break;
        case SRC_STEPS:
            ((int (*)(char *, const char *, ...))fw[FW_SPRINTF])(text, "%u", steps);
            set_text(face->labels[index], text);
            break;
        case SRC_HEART:
            if ((hr.flags & 1u) && hr.bpm)
                ((int (*)(char *, const char *, ...))fw[FW_SPRINTF])(text, "%u", (unsigned)hr.bpm);
            else
                ((int (*)(char *, const char *, ...))fw[FW_SPRINTF])(text, "--");
            set_text(face->labels[index], text);
            break;
        case SRC_BATTERY:
            if (sync)
                ((int (*)(char *, const char *, ...))fw[FW_SPRINTF])(text, "%u", (unsigned)pct);
            else
                ((int (*)(char *, const char *, ...))fw[FW_SPRINTF])(text, "--");
            set_text(face->labels[index], text);
            break;
        case SRC_SCALED:
            ((int (*)(char *, const char *, ...))fw[FW_SPRINTF])(text, "%u", (today.calories_milli & 0x7fffffffu) / 1000u);
            set_text(face->labels[index], text);
            break;
        case SRC_CALORIES:
            ((int (*)(char *, const char *, ...))fw[FW_SPRINTF])(text, "%u", calories);
            set_text(face->labels[index], text);
            break;
        case SRC_DISTANCE:
            ((int (*)(char *, const char *, ...))fw[FW_SPRINTF])(text, "%u.%02u", distance / 100u, distance % 100u);
            set_text(face->labels[index], text);
            break;
        case SRC_DISTANCE_INT:
            ((int (*)(char *, const char *, ...))fw[FW_SPRINTF])(text, "%u", distance / 100u);
            set_text(face->labels[index], text);
            break;
        case SRC_DISTANCE_UNIT:
            set_text(face->labels[index], distance_unit());
            break;
        case SRC_AMPM:
            set_text(face->labels[index], ampm < 0 ? "" : ampm ? "PM" : "AM");
            break;
#if HAS_TEMPERATURE
        case SRC_TEMPERATURE:
            if (face->temperature_valid)
                ((int (*)(char *, const char *, ...))fw[FW_SPRINTF])(text, "%d", temperature_value(face));
            else
                ((int (*)(char *, const char *, ...))fw[FW_SPRINTF])(text, "--");
            set_text(face->labels[index], text);
            break;
#endif
        default:
            set_text(face->labels[index], label_defs[index].text);
            break;
        }
    }
}

static void *place_hand(void *parent, const img_dsc_t *src, int x, int y, int pivot_x, int pivot_y)
{
    void *img;
    if (!fw[FW_IMG_CREATE] || !fw[FW_IMG_SRC] || !fw[FW_POS])
        return 0;
    img = ((void *(*)(void *, const void *))fw[FW_IMG_CREATE])(parent, 0);
    if (!img)
        return 0;
    ((void (*)(void *, const void *))fw[FW_IMG_SRC])(img, src);
    /* Official hands are true-color and turn antialias on before the pivot.
       Angle on a compressed ezip image faults the UI. */
    if (fw[FW_ANTIALIAS])
        ((void (*)(void *, int))fw[FW_ANTIALIAS])(img, 1);
    ((void (*)(void *, int, int))fw[FW_POS])(img, x, y);
    if (fw[FW_PIVOT])
        ((void (*)(void *, int, int))fw[FW_PIVOT])(img, pivot_x, pivot_y);
    if (fw[FW_GLUE])
        ((void (*)(void *, int))fw[FW_GLUE])(img, 1);
    if (fw[FW_PARENT])
        ((void (*)(void *, int))fw[FW_PARENT])(img, 1);
    return img;
}

static void turn_hands(face_t *face, const clock_time_t *now)
{
    int second_angle;
    int minute_angle;
    int hour_angle;
    if (!fw[FW_ANGLE])
        return;
#if HAS_HOUR || HAS_MINUTE || HAS_SECOND
    second_angle = (int)now->sec * 60;
    minute_angle = (int)now->min * 60 + (int)now->sec;
    hour_angle = ((int)now->hour % 12) * 300 + (int)now->min * 5;
#if HAS_SECOND
    if (face->second_hand)
        ((void (*)(void *, int))fw[FW_ANGLE])(face->second_hand, second_angle);
#endif
#if HAS_MINUTE
    if (face->minute_hand)
        ((void (*)(void *, int))fw[FW_ANGLE])(face->minute_hand, minute_angle);
#endif
#if HAS_HOUR
    if (face->hour_hand)
        ((void (*)(void *, int))fw[FW_ANGLE])(face->hour_hand, hour_angle);
#endif
#else
    (void)face;
    (void)now;
    (void)second_angle;
    (void)minute_angle;
    (void)hour_angle;
#endif
}

static int face_init(void *parent)
{
    face_t *face;
    if (!fw[FW_GET_DATA])
        return 0;
    face = ((void *(*)(const char *, int))fw[FW_GET_DATA])(FACE, sizeof(face_t));
    if (!face)
        return 0;
    face->task = 0;
    face->background = 0;
#if HAS_TEMPERATURE || HAS_WEATHER
    face->weather_key = (unsigned)-1;
    face->temperature_c = 0;
    face->temperature_valid = 0;
#endif
    if (fw[FW_IMG_CREATE] && fw[FW_IMG_SRC] && fw[FW_POS] && fw[FW_GLUE] && fw[FW_PARENT]) {
        face->background = ((void *(*)(void *, const void *))fw[FW_IMG_CREATE])(parent, 0);
        if (face->background) {
            ((void (*)(void *, const void *))fw[FW_IMG_SRC])(face->background, &vscode_bg);
            ((void (*)(void *, int, int))fw[FW_POS])(face->background, 0, 0);
            ((void (*)(void *, int))fw[FW_GLUE])(face->background, 1);
            ((void (*)(void *, int))fw[FW_PARENT])(face->background, 1);
        }
    }
    face->hour_hand = 0;
    face->minute_hand = 0;
    face->second_hand = 0;
    face->animation_ticks = 0;
#ifdef DRAW_ORDER_COUNT
    /* LVGL siblings paint in creation order. Create complete elements in
       the exact same stable layer order as the editor; no new firmware API. */
#if GLYPH_SLOT_COUNT
    for (int slot = 0; slot < GLYPH_SLOT_COUNT; slot++)
        face->glyphs[slot] = 0;
#endif
#if ANIMATION_COUNT
    for (int index = 0; index < ANIMATION_COUNT; index++) {
        face->animations[index].image = 0;
        face->animations[index].frame = (unsigned)-1;
    }
#endif
    for (int order = 0; order < DRAW_ORDER_COUNT; order++) {
        int index = draw_items[order].index;
        switch (draw_items[order].kind) {
#if HAS_HOUR
        case 1:
            face->hour_hand = place_hand(parent, &hour_hand, HOUR_POS_X, HOUR_POS_Y, HOUR_PIVOT_X, HOUR_PIVOT_Y);
            break;
#endif
#if HAS_MINUTE
        case 2:
            face->minute_hand = place_hand(parent, &minute_hand, MINUTE_POS_X, MINUTE_POS_Y, MINUTE_PIVOT_X, MINUTE_PIVOT_Y);
            break;
#endif
#if HAS_SECOND
        case 3:
            face->second_hand = place_hand(parent, &second_hand, SECOND_POS_X, SECOND_POS_Y, SECOND_PIVOT_X, SECOND_PIVOT_Y);
            break;
#endif
#if GLYPH_LINE_COUNT
        case 4: {
            const img_dsc_t *blank = glyph_for(glyph_lines[index].set, ' ');
            for (int slot = 0; slot < glyph_lines[index].slots; slot++)
                face->glyphs[glyph_lines[index].slot + slot] = place_still(parent, blank,
                    glyph_lines[index].x + slot * glyph_lines[index].pitch, glyph_lines[index].y);
            break;
        }
#endif
#if ANIMATION_COUNT
        case 5:
            face->animations[index].image = place_still(parent, animation_defs[index].frames[0],
                animation_defs[index].x + animation_defs[index].offsets[0],
                animation_defs[index].y + animation_defs[index].offsets[1]);
            break;
#endif
#if LABEL_COUNT
        case 6:
            face->labels[index] = make_label(parent, label_defs[index].x, label_defs[index].y,
                label_defs[index].color, label_defs[index].size);
            break;
#endif
        default: break;
        }
    }
#else
#if HAS_HOUR
    face->hour_hand = place_hand(parent, &hour_hand, HOUR_POS_X, HOUR_POS_Y, HOUR_PIVOT_X, HOUR_PIVOT_Y);
#endif
#if HAS_MINUTE
    face->minute_hand = place_hand(parent, &minute_hand, MINUTE_POS_X, MINUTE_POS_Y, MINUTE_PIVOT_X, MINUTE_PIVOT_Y);
#endif
#if HAS_SECOND
    face->second_hand = place_hand(parent, &second_hand, SECOND_POS_X, SECOND_POS_Y, SECOND_PIVOT_X, SECOND_PIVOT_Y);
#endif
#if GLYPH_LINE_COUNT
    for (int slot = 0; slot < GLYPH_SLOT_COUNT; slot++)
        face->glyphs[slot] = 0;
    for (int line = 0; line < GLYPH_LINE_COUNT; line++) {
        const img_dsc_t *blank = glyph_for(glyph_lines[line].set, ' ');
        for (int index = 0; index < glyph_lines[line].slots; index++) {
            face->glyphs[glyph_lines[line].slot + index] = place_still(
                parent,
                blank,
                glyph_lines[line].x + index * glyph_lines[line].pitch,
                glyph_lines[line].y);
        }
    }
#endif
#if ANIMATION_COUNT
    for (int index = 0; index < ANIMATION_COUNT; index++) {
        face->animations[index].image = place_still(
            parent,
            animation_defs[index].frames[0],
            animation_defs[index].x + animation_defs[index].offsets[0],
            animation_defs[index].y + animation_defs[index].offsets[1]);
        face->animations[index].frame = (unsigned)-1;
    }
#endif
    if (LABEL_COUNT > 32)
        return 0;
    for (int index = 0; index < LABEL_COUNT; index++) {
        face->labels[index] = make_label(
            parent,
            label_defs[index].x,
            label_defs[index].y,
            label_defs[index].color,
            label_defs[index].size);
    }
#endif
    tick(0);
    return 0;
}

static int face_pause(void)
{
    face_t *face;
    if (!fw[FW_GET_DATA])
        return 0;
    face = ((void *(*)(const char *, int))fw[FW_GET_DATA])(FACE, sizeof(face_t));
    if (face && face->task && fw[FW_TASK_DEL]) {
        ((void (*)(void *))fw[FW_TASK_DEL])(face->task);
        face->task = 0;
    }
    return 0;
}

static int face_resume(void)
{
    face_t *face;
    if (!fw[FW_GET_DATA])
        return 0;
    face = ((void *(*)(const char *, int))fw[FW_GET_DATA])(FACE, sizeof(face_t));
    if (face && !face->task && fw[FW_TASK_CREATE]) {
#if HAS_HOUR || HAS_MINUTE || HAS_SECOND || ANIMATION_COUNT
        face->task = ((void *(*)(void (*)(void *), int, int, void *))fw[FW_TASK_CREATE])(tick, 200, 3, 0);
#else
        face->task = ((void *(*)(void (*)(void *), int, int, void *))fw[FW_TASK_CREATE])(tick, 1000, 3, 0);
#endif
        tick(0);
    }
    return 0;
}

static int face_deinit(void)
{
    face_t *face;
    if (!fw[FW_GET_DATA])
        return 0;
    face = ((void *(*)(const char *, int))fw[FW_GET_DATA])(FACE, sizeof(face_t));
    if (!face)
        return 0;
    if (face->task && fw[FW_TASK_DEL])
        ((void (*)(void *))fw[FW_TASK_DEL])(face->task);
    if (fw[FW_FREE_DATA])
        ((void (*)(const char *))fw[FW_FREE_DATA])(FACE);
    return 0;
}

static const void *ops[] = {
    face_init,
    face_pause,
    face_resume,
    face_deinit,
};

/* Official LS11 dial modules attach a built-in resource manager before they
   register the face.  It owns the second ELF (dial_*_res.so) and is required
   when an image descriptor points at .ROM3_IMG data in that ELF.  Small test
   faces can appear to work without it; imported faces with glyphs and frame
   sequences consistently exercise the missing path. */
int module_init(void *module)
{
    void *resource_manager = 0;
    if (!fw[FW_REGISTER])
        return -1;
    if (module && fw[FW_MALLOC] && fw[FW_SET_RES_MNG] && fw[FW_RES_MNG_INIT]) {
        resource_manager = ((void *(*)(unsigned))fw[FW_MALLOC])(44);
        if (resource_manager) {
            ((void (*)(void *, void *))fw[FW_SET_RES_MNG])(module, resource_manager);
            ((void *(*)(void *, void *))fw[FW_RES_MNG_INIT])(
                resource_manager,
                fw[FW_LANG_PACK]);
            if (fw[FW_GET_LOCALE] && fw[FW_SET_LOCALE]) {
                const char *locale = ((const char *(*)(void))fw[FW_GET_LOCALE])();
                if (locale)
                    ((int (*)(void *, const char *))fw[FW_SET_LOCALE])(resource_manager, locale);
            }
        }
    }
    return ((int (*)(const char *, const char *, const void *))fw[FW_REGISTER])(FACE, TITLE, ops);
}

int module_cleanup(void)
{
    void *resource_manager = 0;
    if (fw[FW_GET_RES_MNG])
        resource_manager = ((void *(*)(void))fw[FW_GET_RES_MNG])();
    if (resource_manager && fw[FW_RES_MNG_DESTROY])
        ((void (*)(void *))fw[FW_RES_MNG_DESTROY])(resource_manager);
    if (resource_manager && fw[FW_FREE])
        ((void (*)(void *))fw[FW_FREE])(resource_manager);
    return 0;
}
