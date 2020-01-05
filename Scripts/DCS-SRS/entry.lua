declare_plugin("DCS-SRS", {
  installed     = true,
  dirName       = current_mod_path,
  binaries      = {
   "srs.dll"
  },

  version       = "1.7.2.0",
  state         = "installed",
  developerName = "github.com/ciribob/DCS-SimpleRadioStandalone",
  info          = _("DCS-SimpleRadio Standalone\n\nBrings realistic VoIP comms to DCS with a cockpit integration with every aircraft\n\nCheck Special Settings for SRS integration settings\n\nSRS Discord for Support: https://discord.gg/baw7g3t"),

  Skins = {
    {
      name = "DCS-SRS",
      dir  = "Theme"
    },
  },

  Options = {
    {
      name   = "DCS-SRS",
      nameId = "DCS-SRS",
      dir    = "Options",
    },
  },
})

plugin_done()
