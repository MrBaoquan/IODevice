
// IOMFCTest.h : PROJECT_NAME Ӧ�ó������ͷ�ļ�
//

#pragma once

#ifndef __AFXWIN_H__
	#error "�ڰ������ļ�֮ǰ������stdafx.h�������� PCH �ļ�"
#endif

#include "resource.h"		// ������


// CIOMFCTestApp: 
// �йش����ʵ�֣������ IOMFCTest.cpp
//

class CIOMFCTestApp : public CWinApp
{
public:
	CIOMFCTestApp();

// ��д
public:
	virtual BOOL InitInstance();

// ʵ��

	DECLARE_MESSAGE_MAP()
};

extern CIOMFCTestApp theApp;