-- Version 1.0.6.0
-- Make sure you COPY this file to the same location as the Export.lua as well.
SRS = {}

SRS.dbg = {}
SRS.logFile = io.open(lfs.writedir()..[[Logs\DCS-SRS-GameGUI.log]], "w")
function SRS.log(str)
    if SRS.logFile then
        SRS.logFile:write(str.."\n")
        SRS.logFile:flush()
    end
end

package.path  = package.path..";.\\LuaSocket\\?.lua"
package.cpath = package.cpath..";.\\LuaSocket\\?.dll"

local socket = require("socket")

local JSON = loadfile("Scripts\\JSON.lua")()
SRS.JSON = JSON

SRS.UDPSendSocket = socket.udp()
SRS.UDPSendSocket:settimeout(0)

SRS.onPlayerChangeSlot = function(id)

    local _update = {
        name = "",
        side = 0,
    }

    _update.name = net.get_player_info(id, "name" )
	_update.side = net.get_player_info(id,'side')

    SRS.log("Selected Slot  ID:"..id.." Name: ".._update.name.." Side: ".._update.side)

    socket.try(SRS.UDPSendSocket:sendto(SRS.JSON:encode(_update).." \n", "239.255.50.10", 5068))
end


DCS.setUserCallbacks(SRS)

net.log("Loaded - DCS-SRS GameGUI")