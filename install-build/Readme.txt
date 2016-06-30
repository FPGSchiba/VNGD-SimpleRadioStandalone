Extract all the files from the Zip before running the Installer!

Installer needs to be run by CLIENTS ONLY

Dedicated servers DO NOT need the installer running and will cause issues if the dedicated is on the same network as the client :)

To Install Manually

Copy DCS-SimpleRadioStandalone.lua to C:\Users\USERNAME\Saved Games\DCS\Scripts
Create the folder if it doesnt exist

Add:

local dcsSr=require('lfs');dofile(dcsSr.writedir()..[[Scripts\DCS-SimpleRadioStandalone.lua]])

To the END of the Export.lua file in C:\Users\USERNAME\Saved Games\DCS\Scripts

If it doesnt exist, just create the file and add the single line to it.

Copy the rest of the zip file where ever you like and then run, don't forget to keep opus.dll with the rest of the .exes

Thread on Forums: http://forums.eagle.ru/showthread.php?t=169387