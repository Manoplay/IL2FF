namespace Enums
{
    public enum EGooeyTextStyle: int
    {
        GTS_T1 = 0,
        GTS_T2 = 1,
        GTS_T3 = 2,
        GTS_T4 = 3,
        GTS_T5 = 4,
        GTS_T5i = 5,
        GTS_B1 = 6,
        GTS_SMin = 7,
        GTS_SMax = 15,
        GTS_T0 = 16,
        GTS_TM1 = 17,
        GTS_SMALL_PRINT = 18
    }

    public enum EGooeyButtonState: int
    {
        STATE_NORMAL = 0,
        STATE_TOGGLE = 1,
        STATE_DISABLED = 2,
        STATE_INVISIBLE = 3,
        STATE_FLASHING = 4,
        STATE_DEMO_DISABLED = 5,
        STATE_FADED = 6,
        STATE_NO_UNDERLINE = 7,
        STATE_LAST = 8
    }

    public enum ESwitchEvent: int
    {
        SWITCH_EVENT_UPDATE = 0,
        SWITCH_EVENT_REGISTER = 1,
        SWITCH_EVENT_UNREGISTER = 2	
    }

    public enum EButtonPrompts: int
    {
        BP_MENU_SELECT = 0,
        BP_MENU_CANCEL = 1,
        BP_CROSS = 2,
        BP_CIRCLE = 3,
        BP_SQUARE = 4,
        BP_TRIANGLE = 5,
        BP_LEFTSTICK = 6,
        BP_RIGHTSTICK = 7,
        BP_DPAD = 8,
        BP_DPAD_LEFT = 9,
        BP_DPAD_RIGHT = 10,
        BP_DPAD_UP = 11,
        BP_DPAD_DOWN = 12,
        BP_L1 = 13,
        BP_L2 = 14,
        BP_L1_L2 = 15,
        BP_L1_R1 = 16,
        BP_R1 = 17,
        BP_R2 = 18,
        BP_R1_R2 = 19,
        BP_L2_R2 = 20,
        BP_L3 = 21,
        BP_R3 = 22,
        BP_SELECT = 23,
        BP_START = 24,
        BP_SHAKE = 25,
        BP_LAST = 26
    }

    public enum EPlayerNumber: int
    {
        E_PLAYER_NUMBER_NONE = -1,
        E_PLAYER_NUMBER_0 = 0,
        E_PLAYER_NUMBER_1 = 1,
        E_PLAYER_NUMBER_2 = 2,
        E_PLAYER_NUMBER_3 = 3,
        E_PLAYER_NUMBER_4 = 4,
        E_PLAYER_NUMBER_5 = 5,
        E_PLAYER_NUMBER_6 = 6,
        E_PLAYER_NUMBER_7 = 7,
        E_PLAYER_NUMBER_8 = 8,
        E_PLAYER_NUMBER_9 = 9,
        E_PLAYER_NUMBER_10 = 10
    }

    public enum ETutorialLevels: int
    {
        TUT_LEVEL_UNKNOWN = 0,	
        TUT_LEVEL_NONE = 1,
        TUT_LEVEL_FIRST = 2,	
        TUT_LEVEL_10_MOVING_OBJECTS = 2,
        TUT_LEVEL_11_CREATING_AND_DELETING_OBJECTS = 3,
        TUT_LEVEL_13_ADDING_MUSIC = 4,
        TUT_LEVEL_15_CRAFT_MATERIALS_PART1 = 5,
        TUT_LEVEL_16_CORNER_EDITING_TOOL = 6,
        TUT_LEVEL_17_CRAFT_MATERIALS_PART2 = 7,	
        TUT_LEVEL_18_FLOOD_FILL_TOOL = 8,
        TUT_LEVEL_19_SOUND_OBJECTS = 9,
        TUT_LEVEL_21_MAGIC_MOUTHS = 10,
        TUT_LEVEL_29_DANGEROUS_STUFF = 11,
        TUT_LEVEL_30_BOLTS = 12,
        TUT_LEVEL_31_SPRUNG_BOLTS = 13,
        TUT_LEVEL_32_STRING = 14,
        TUT_LEVEL_33_ELASTIC = 15,
        TUT_LEVEL_34_RODS = 16,
        TUT_LEVEL_35_SPRINGS = 17,
        TUT_LEVEL_36_MOTORIZED_BOLTS = 18,
        TUT_LEVEL_37_OSCILLATOR_BOLTS = 19,
        TUT_LEVEL_38_CHAINS = 20,
        TUT_LEVEL_39_PISTONS = 21,
        TUT_LEVEL_40_EMITTERS = 22,	
        TUT_LEVEL_41_BUTTON_SWITCHES = 23,
        TUT_LEVEL_42_TWO_WAY_SWITCHES = 24,	
        TUT_LEVEL_43_STICKER_SWITCHES = 25	,
        TUT_LEVEL_44_GRAB_SWITCH = 26	,
        TUT_LEVEL_45_THREE_WAY_SWITCHES = 27,	
        TUT_LEVEL_46_PROXIMITY_SWITCHES = 28,	
        TUT_LEVEL_47_MAGNETIC_KEY_SWITCHES = 29,	
        TUT_LEVEL_48_DISSOLVE_MATERIAL = 30	,
        TUT_LEVEL_49_EXPLOSIVE_OBJECTS = 31	,
        TUT_LEVEL_50_ROCKETS = 32	,
        TUT_LEVEL_LAST = 33	,
        TUT_LEVEL_51_CREATURE_KIT = 33,	
        TUT_VIDEO_CAMERA_ZONES = 34	,
        TUT_VIDEO_CAPTURE_OBJECTS = 35,	
        TUT_VIDEO_CHECKPOINTS = 36	,
        TUT_VIDEO_ENTRANCE = 37	,
        TUT_VIDEO_FLOATY = 38	,
        TUT_VIDEO_GLOBAL_CONTROLS = 39,	
        TUT_VIDEO_KEYS = 40	,
        TUT_VIDEO_POINT_BUBBLES = 41,	
        TUT_VIDEO_PRIZE_BUBBLES = 42,	
        TUT_VIDEO_RACE_KIT = 43	,
        TUT_VIDEO_SCORING_POST = 44,	
        TUT_VIDEO_EYETOY = 45	,
        TUT_VIDEO_SNAPSHOT_CAM = 46,	
        TUT_VIDEO_MAGIC_EYE = 47	,
        TUT_VIDEO_NO_JOIN_POST = 48	,
        TUT_LEVEL_COUNT = 49
    }
}
