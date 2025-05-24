namespace Vanguard.VCS.Client.Input
{
    public class InputBindState
    {
        public InputDevice MainDevice { get; set; }
        public bool MainDeviceState { get; set; }

        public InputDevice ModifierDevice { get; set; }

        public bool ModifierState { get; set; }

        //overall state of bind - True or false being on or false
        public bool IsActive { get; set; }
    }
}