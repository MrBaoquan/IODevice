#pragma once
#include <iostream>

std::string BSTR2String(BSTR InStr)
{
	std::wstring _wStr = InStr;
	return std::string(_wStr.begin(), _wStr.end());
}

BSTR string2BSTR(std::string InStr) 
{
	return SysAllocString(std::wstring(InStr.begin(),InStr.end()).c_str());
}