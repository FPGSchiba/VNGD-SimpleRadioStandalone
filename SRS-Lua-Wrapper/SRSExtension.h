#pragma once

#include <WinSock2.h>
#include <string>
#include <map>
#include <thread>
#include <windows.h>
#include <tlhelp32.h>
namespace SRS
{
	

	class SRSExtension
	{
	public:
		SRSExtension();
		~SRSExtension();

		static bool Launch(const char*);
		static std::wstring ReadSRSPath();
		static bool IsSRSRunning();
	private:
		static std::wstring s2ws(const std::string& str);
		
	};

}
