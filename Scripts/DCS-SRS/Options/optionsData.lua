--package.cpath = package.cpath..";"..lfs.writedir().."Mods\\tech\\DCS-SRS\\bin\\?.dll;"

cdata = {
  DCS_SRS = _("DCS-SimpleRadio Standlone Configuration"),
  DCS_SRS_CLIENT_PATH_LABEL = _("DCS-SRS Client Path"),
  DCS_SRS_CLIENT_PATH = _("Make sure to set the path to SRS using the \"Set SRS Path for DCS Setting\""),
  DCS_SRS_OVERLAY = _("Show DCS Overlay by Default"),
  DCS_SRS_OVERLAY_INFO = _("If checked - SRS DCS overlay will be on by default - if not use Left Ctrl + Left Shift + ESC to show"),
  DCS_SRS_AUTO_LAUNCH = _("Auto Launch SRS"),
  DCS_SRS_AUTO_LAUNCH_INFO = _("If checked - and you connect to a server with SRS Autoconnect on, SRS will launch. "),
  DCS_SRS_AUTO_LAUNCH_INFO_2 = _("NOTE: Make sure to set the path to SRS using the \"Set SRS Path for DCS Setting\" if SRS doesnt start")
}
