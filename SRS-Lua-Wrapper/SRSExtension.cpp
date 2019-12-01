#include "SRSExtension.h"


SRS::SRSExtension::SRSExtension()
{

}

bool SRS::SRSExtension::Launch(const char* host)
{
	if (!IsSRSRunning())
	{
		std::string directory = ReadSRSPath();
		std::string path = directory;
		path = path.append("\\SR-ClientRadio.exe");

		if (!directory.empty())
		{
			std::string hostStr = std::string(host).insert(0, "-host=");

			ShellExecute(NULL, L"open", s2ws(path).c_str(), s2ws(hostStr).c_str(), s2ws(directory).c_str(), SW_SHOWDEFAULT);
			return true;
		}

	}
	return false;
}


std::string SRS::SRSExtension::ReadSRSPath()
{
	//SRPathStandalone
	HKEY hKey = 0;
	DWORD bufferSize = 512;
	LPVOID regBuffer = new char[bufferSize];
	DWORD dwType = 0;
	if (RegOpenKey(HKEY_CURRENT_USER, L"SOFTWARE\\DCS-SR-Standalone", &hKey) == ERROR_SUCCESS)
	{
		dwType = REG_SZ;
		if (RegQueryValueEx(hKey, L"SRPathStandalone", 0, &dwType, (BYTE*)regBuffer, &bufferSize) == ERROR_SUCCESS)
		{

			//its a 2 Byte CHAR! 
			WCHAR* locationStr = reinterpret_cast<WCHAR*>(regBuffer);
			locationStr[bufferSize] = 0; //add terminator

										 //convert to widestring
			std::wstring ws(locationStr);
			//convert to normal string
			std::string str(ws.begin(), ws.end());

			RegCloseKey(hKey);

			delete[] regBuffer;
			return str;
		}

		RegCloseKey(hKey);
	}

	delete[] regBuffer;

	return "";


}

bool SRS::SRSExtension::IsSRSRunning()
{
	bool exists = false;
	PROCESSENTRY32 entry;
	entry.dwSize = sizeof(PROCESSENTRY32);

	HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, NULL);

	if (Process32First(snapshot, &entry))
		while (Process32Next(snapshot, &entry))
			if (!_wcsicmp(entry.szExeFile, L"SR-ClientRadio.exe"))
				exists = true;

	CloseHandle(snapshot);
	return exists;

}

//https://stackoverflow.com/questions/8468597/prepend-stdstring
std::wstring SRS::SRSExtension::s2ws(const std::string& str)
{
	int size_needed = MultiByteToWideChar(CP_UTF8, 0, &str[0], (int)str.size(), NULL, 0);
	std::wstring wstrTo(size_needed, 0);
	MultiByteToWideChar(CP_UTF8, 0, &str[0], (int)str.size(), &wstrTo[0], size_needed);
	return wstrTo;
}

SRS::SRSExtension::~SRSExtension()
{
}
