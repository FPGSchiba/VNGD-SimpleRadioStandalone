local base = _G

package.path  = package.path..";.\\LuaSocket\\?.lua;"..'.\\Scripts\\?.lua;'.. '.\\Scripts\\UI\\?.lua;'
package.cpath = package.cpath..";.\\LuaSocket\\?.dll;"

local JSON = loadfile("Scripts\\JSON.lua")()

module("srs_overlay")

local require           = base.require
local os                = base.os
local io                = base.io
local table             = base.table
local string            = base.string
local math              = base.math
local assert            = base.assert
local pairs             = base.pairs

local lfs               = require('lfs')
local socket            = require("socket") 
local net               = require('net')
local DCS               = require("DCS") 
local U                 = require('me_utilities')
local Skin              = require('Skin')
local Gui               = require('dxgui')
local DialogLoader      = require('DialogLoader')
local EditBox           = require('EditBox')
local Tools             = require('tools')

local _modes = {     
    hidden = "hidden",
    minimum = "minimum",
    full = "full",
}

local _isWindowCreated = false
local _listenSocket = {}
local _radioInfo = {}
local _listStatics = {} -- placeholder objects
local _listMessages = {} -- data

local WIDTH = 300
local HEIGHT = 130

local srsOverlay = { 
    connection = nil,
    logFile = io.open(lfs.writedir()..[[Logs\DCS-SRS-InGameRadio.log]], "w")
}

function srsOverlay.loadConfiguration()
    srsOverlay.log("Loading config file...")
    local tbl = Tools.safeDoFile(lfs.writedir() .. 'Config/SRSConfig.lua', false)
    if (tbl and tbl.config) then
        srsOverlay.log("Configuration exists...")
        srsOverlay.config = tbl.config
    else
        srsOverlay.log("Configuration not found, creating defaults...")
        srsOverlay.config = { 
            mode = "full",
            hotkey = "Ctrl+Shift+escape",
            windowPosition = { x = 66, y = 13 }
        }
        srsOverlay.saveConfiguration()
    end      
end

function srsOverlay.saveConfiguration()
    U.saveInFile(srsOverlay.config, 'config', lfs.writedir() .. 'Config/SRSConfig.lua')
end

function srsOverlay.log(str)
    if not str then 
        return
    end

    if srsOverlay.logFile then
        srsOverlay.logFile:write("["..os.date("%H:%M:%S").."] "..str.."\r\n")
        srsOverlay.logFile:flush()
    end
end

function srsOverlay.updateRadio()    

	_listMessages = {}

	if _radioInfo and _radioInfo.radios then
		for _i,_radio in pairs(_radioInfo.radios) do

			-- TODO handle "Current"

			
			local fullMessage

			if _radio.modulation == 3 then
					 fullMessage = ""
					
			elseif _radio.modulation == 2 then 

					 fullMessage = "INTERCOM "
				
			else
					 fullMessage = _radio.name.." - "

					 fullMessage = fullMessage..string.format("%.3f", _radio.freq/1000000.0)

         --            srsOverlay.log( _radio.freq)

					 if _radio.modulation == 0 then
						fullMessage = fullMessage.." AM"
					 else
						fullMessage = fullMessage.." FM"
					 end

					 if _radio.secFreq > 100 then
						fullMessage = fullMessage.." G"
					 end

					 if _radio.enc and _radio.encKey > 0 then
						fullMessage = fullMessage.." E".._radio.encKey
					 end
			end

			local _selected = _i == (_radioInfo.selected+1)

			if _selected then
				fullMessage = fullMessage.." * "
            end

       --     srsOverlay.log(fullMessage)


    
			local msg = {message = fullMessage, skin = typesMessage.normal, height = 20}
			table.insert(_listMessages, msg)
		end
	end

    srsOverlay.paintRadio()
end


function srsOverlay.paintRadio()

    local offset = 0
    for k,v in pairs(_listStatics) do
        v:setText("")
     --   v:setSkin(typesMessage.normal)
       -- v:setBounds(0,offset,WIDTH-20,20)

       -- offset = offset +20

    end
   

    local curStatic = 1
    offset = 0

    for _i,_msg in pairs(_listMessages) do

        if(_msg~=nil and _msg.message ~= nil and  _listStatics[curStatic] ~= nil ) then
            _listStatics[curStatic]:setSkin(_msg.skin)
            _listStatics[curStatic]:setBounds(0,offset,WIDTH-10,_msg.height)
            _listStatics[curStatic]:setText(_msg.message)

            --10 padding
            offset = offset +20
            curStatic = curStatic +1
        end

    end

--    if _listMessages[curMsg] then
--        while curMsg <= #_listMessages   do
--            local msg = _listMessages[curMsg]
--            _listStatics[curStatic]:setSkin(msg.skin)
--            _listStatics[curStatic]:setBounds(0,offset,WIDTH-50,msg.height)
--            _listStatics[curStatic]:setText(msg.message)
--            offset = offset + msg.height +10 --10 padding
--            curMsg = curMsg + 1
--            curStatic = curStatic + 1
--            num = num + 1
--        end
--    end
end

function srsOverlay.createWindow()
    window = DialogLoader.spawnDialogFromFile(lfs.writedir() .. 'Scripts\\srs_overlay.dlg', cdata)

    box         = window.Box
    pNoVisible  = window.pNoVisible
    pDown       = box.pDown
    pMsg        = box.pMsg
    
    window:addHotKeyCallback(srsOverlay.config.hotkey, srsOverlay.onHotkey)

    
    skinModeFull = pNoVisible.windowModeFull:getSkin()
    skinMinimum = pNoVisible.windowModeMin:getSkin()

    typesMessage =
    {
        normal         = pNoVisible.eYellowText:getSkin(),
        receive         = pNoVisible.eWhiteText:getSkin(),
    }

    
    _listStatics = {}
    
    for i = 1, 4 do
        local staticNew = EditBox.new()        
        table.insert(_listStatics, staticNew)
        staticNew:setReadOnly(true)   
        staticNew:setTextWrapping(true)  
        staticNew:setMultiline(true) 
        pMsg:insertWidget(staticNew)
    end

    w, h = Gui.GetWindowSize()
            
    srsOverlay.resize(w, h)
    
    srsOverlay.setMode(srsOverlay.config.mode)    
    
    window:addPositionCallback(srsOverlay.positionCallback)     
    srsOverlay.positionCallback()

    _isWindowCreated = true

    srsOverlay.log("Window created")
--
--    srsOverlay.addMessage("124.00 *", "AN/ARC-186(V)", typesMessage.sys)
 --   srsOverlay.addMessage("256.00", "AN/ARC-164 UHF", typesMessage.msg)
  --  srsOverlay.addMessage("10.00", "AN/ARC-186(V)FM", typesMessage.msg)
  --  srsOverlay.addMessage("10.00", "INTERCOM", typesMessage.msg)
end

function srsOverlay.setVisible(b)
    window:setVisible(b)
end

function srsOverlay.setMode(mode)
    srsOverlay.log("setMode called "..mode)
    srsOverlay.config.mode = mode 
    
    if window == nil then
        return
    end

    srsOverlay.setVisible(true) -- if you make the window invisible, its destroyed
    
    if srsOverlay.config.mode == _modes.hidden then

        box:setVisible(false)
        pDown:setVisible(false)

    else
        box:setVisible(true)
        window:setSize(WIDTH, HEIGHT)

        if srsOverlay.config.mode == _modes.minimum then

            box:setSkin(skinMinimum)

            pDown:setVisible(false)

            window:setSkin(Skin.windowSkinChatMin())
        end
        
        if srsOverlay.config.mode == _modes.full then
            box:setSkin(skinModeFull)

            box:setVisible(true)
            pDown:setVisible(true)

            window:setSkin(Skin.windowSkinChatWrite())
        end    
    end


    srsOverlay.paintRadio()
    srsOverlay.saveConfiguration()
end

function srsOverlay.getMode()
    return srsOverlay.config.mode
end

function srsOverlay.onHotkey()

    if (srsOverlay.getMode() == _modes.full) then
        srsOverlay.setMode(_modes.minimum)
    elseif (srsOverlay.getMode() == _modes.minimum) then
        srsOverlay.setMode(_modes.hidden)
    else
        srsOverlay.setMode(_modes.full)
    end 
end

function srsOverlay.resize(w, h)
    window:setBounds(srsOverlay.config.windowPosition.x, srsOverlay.config.windowPosition.y, WIDTH, HEIGHT)
    box:setBounds(0, 0, WIDTH, HEIGHT)
end

function srsOverlay.positionCallback()
    local x, y = window:getPosition()

    x = math.max(math.min(x, w-WIDTH), 0)
    y = math.max(math.min(y, h-HEIGHT), 0)

    window:setPosition(x, y)

    srsOverlay.config.windowPosition = { x = x, y = y }
    srsOverlay.saveConfiguration()
end

function srsOverlay.show(b)
    if _isWindowCreated == false then
        srsOverlay.createWindow()
    end
    
    if b == false then
        srsOverlay.saveConfiguration()
    end
    
    srsOverlay.setVisible(b)
end

function srsOverlay.initListener()

_listenSocket = socket.udp()

--bind for listening for Radio info
_listenSocket:setsockname("*", 7080)
_listenSocket:settimeout(0) 

end

function srsOverlay.listen()

    -- Receive buffer is 8192 in LUA Socket
    -- will contain 10 clients for LOS
    local _received = _listenSocket:receive()

    if _received then
        local _decoded = JSON:decode(_received)

        srsOverlay.log(_received)

        if _decoded then

            srsOverlay.log("Decoded ")
            _radioInfo = _decoded
        end
    end
end


function srsOverlay.onSimulationFrame()
    if srsOverlay.config == nil then
        srsOverlay.loadConfiguration()
    end

    if not window then 
        srsOverlay.log("Creating window...")
        srsOverlay.show(true)
        srsOverlay.setMode(_modes.minimum)

        -- init connection
        srsOverlay.initListener()

    end

    srsOverlay.listen()

    srsOverlay.updateRadio()
    
end 

DCS.setUserCallbacks(srsOverlay)

net.log("Loaded - DCS-SRS Overlay GameGUI")