namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Settings
{
    public enum SettingType
    {
        RadioEffects = 0,
        Radio1Channel = 1,
        Radio2Channel = 2,
        Radio3Channel = 3,
        Radio4Channel = 12,
        Radio5Channel = 13,
        Radio6Channel = 14,
        Radio7Channel = 15,
        Radio8Channel = 16,
        Radio9Channel = 17,
        Radio10Channel = 18,
        RadioSwitchIsPTT = 4,
        IntercomChannel = 5,
        RadioClickEffects = 6, // Recieving Radio Effects 
        RadioClickEffectsTx = 7, //Transmitting Radio Effects
        RadioEncryptionEffects = 8, //Radio Encryption effects
        ResampleOutput = 9, //not used - on always
        AutoConnectPrompt = 10, //message about auto connect
        RadioOverlayTaskbarHide = 11,
        RefocusDCS = 19,
        ExpandControls = 20,

        RadioRxEffects_Start = 21, // Recieving Radio Effects 
        RadioRxEffects_End = 22,
        RadioTxEffects_Start = 23, // Recieving Radio Effects 
        RadioTxEffects_End = 24,
       
    }
}