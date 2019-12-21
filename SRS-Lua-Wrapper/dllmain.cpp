// dllmain.cpp : Defines the entry point for the DLL application.
#pragma comment(lib, "lua.lib")
#include "SRSExtension.h"


extern "C" {
#include "include/lua.h"
#include "include/lualib.h"
#include "include/lauxlib.h"
}
static int start_srs(lua_State* L) {


	const char* host = luaL_checkstring(L, 1);
	lua_pushboolean(L, SRS::SRSExtension::Launch(host));
	
	return 1;
}

static int get_srs_path(lua_State* L) {
	std::wstring wstr = SRS::SRSExtension::ReadSRSPath();
	std::string str(wstr.begin(), wstr.end());
	lua_pushlstring(L, str.c_str(), str.size());
	return 1;
}

extern "C" int __declspec(dllexport) luaopen_srs(lua_State * L) {
	static const luaL_Reg Map[] = {
		{ "start_srs", start_srs },
		{ "get_srs_path", get_srs_path },
		{ NULL,NULL }
	};
	luaL_register(L, "srs", Map);
	return 1;
}
