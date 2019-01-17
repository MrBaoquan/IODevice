
// IOMFCTestDlg.cpp : 实现文件
//

#include "stdafx.h"
#include "IOMFCTest.h"
#include "IOMFCTestDlg.h"
#include "afxdialogex.h"

#pragma comment(lib,"IODevice.lib")
#include "IOSettings.h"

#ifdef _DEBUG
#define new DEBUG_NEW
#endif


// 用于应用程序“关于”菜单项的 CAboutDlg 对话框

class CAboutDlg : public CDialogEx
{
public:
	CAboutDlg();

// 对话框数据
#ifdef AFX_DESIGN_TIME
	enum { IDD = IDD_ABOUTBOX };
#endif

	protected:
	virtual void DoDataExchange(CDataExchange* pDX);    // DDX/DDV 支持

// 实现
protected:
	DECLARE_MESSAGE_MAP()
};

CAboutDlg::CAboutDlg() : CDialogEx(IDD_ABOUTBOX)
{
}

void CAboutDlg::DoDataExchange(CDataExchange* pDX)
{
	CDialogEx::DoDataExchange(pDX);
}

BEGIN_MESSAGE_MAP(CAboutDlg, CDialogEx)
END_MESSAGE_MAP()


// CIOMFCTestDlg 对话框



CIOMFCTestDlg::CIOMFCTestDlg(CWnd* pParent /*=NULL*/)
	: CDialogEx(IDD_IOMFCTEST_DIALOG, pParent)
{
	m_hIcon = AfxGetApp()->LoadIcon(IDR_MAINFRAME);
}

void CIOMFCTestDlg::DoDataExchange(CDataExchange* pDX)
{
	CDialogEx::DoDataExchange(pDX);
}

BEGIN_MESSAGE_MAP(CIOMFCTestDlg, CDialogEx)
	ON_WM_SYSCOMMAND()
	ON_WM_PAINT()
	ON_WM_QUERYDRAGICON()
    ON_WM_TIMER()
END_MESSAGE_MAP()


void Func(DevelopHelper::BYTE arr[])
{
    int size_a = sizeof(arr);
    int size_b = sizeof(arr[0]);
    int result = size_a / size_b;
}

// CIOMFCTestDlg 消息处理程序

BOOL CIOMFCTestDlg::OnInitDialog()
{
	CDialogEx::OnInitDialog();

	// 将“关于...”菜单项添加到系统菜单中。

	// IDM_ABOUTBOX 必须在系统命令范围内。
	ASSERT((IDM_ABOUTBOX & 0xFFF0) == IDM_ABOUTBOX);
	ASSERT(IDM_ABOUTBOX < 0xF000);

	CMenu* pSysMenu = GetSystemMenu(FALSE);
	if (pSysMenu != NULL)
	{
		BOOL bNameValid;
		CString strAboutMenu;
		bNameValid = strAboutMenu.LoadString(IDS_ABOUTBOX);
		ASSERT(bNameValid);
		if (!strAboutMenu.IsEmpty())
		{
			pSysMenu->AppendMenu(MF_SEPARATOR);
			pSysMenu->AppendMenu(MF_STRING, IDM_ABOUTBOX, strAboutMenu);
		}
	}

	// 设置此对话框的图标。  当应用程序主窗口不是对话框时，框架将自动
	//  执行此操作
	SetIcon(m_hIcon, TRUE);			// 设置大图标
	SetIcon(m_hIcon, FALSE);		// 设置小图标

    using namespace DevelopHelper;
    

    DevelopHelper::BYTE arr[20];

    Func(arr);

   // int length = sizeof(arr) / sizeof(arr[0]);


    IOSettings::Instance().SetIOConfigPath("IODevice.xml");
    IODevice& standardDevice = IODeviceController::Instance().GetIODevice("Standard");
    
    //standardDevice.BindKey(EKeys::RightMouseButton, IE_Repeat, this, &CIOMFCTestDlg::OnRMPressed);
    //standardDevice.BindKey(EKeys::RightMouseButton, IE_Released, this, &CIOMFCTestDlg::OnRMReleased);
    //standardDevice.BindKey(EKeys::LeftMouseButton, IE_DoubleClick, this, &CIOMFCTestDlg::OnLMDoubleClick);
    //standardDevice.BindAxis("MoveLR", this, &CIOMFCTestDlg::OnAxis);
    standardDevice.BindKey(EKeys::A, IE_Pressed, this, &CIOMFCTestDlg::OnRMPressed);
    standardDevice.BindKey(EKeys::B, IE_Pressed, this, &CIOMFCTestDlg::OnRMReleased);
    //standardDevice.BindAction("Jump", IE_Pressed, this, &CIOMFCTestDlg::OnActionWithKey3);

    standardDevice.BindAxisKey("Axis_50", this, &CIOMFCTestDlg::OnAxis);

   // standardDevice.BindAction("Action2", IE_Pressed, this, &CIOMFCTestDlg::OnActionWithKey2);
    //standardDevice.BindAction("Action3", IE_Pressed, this, &CIOMFCTestDlg::OnActionWithKey3);
    //standardDevice.BindAction("AnyKey", IE_Released, this, &CIOMFCTestDlg::OnReleaseWithKey);
 
    /*standardDevice.BindAxis("AxisTest", this, &CIOMFCTestDlg::OnAxis);*/

    static int a = 1;
    static bool b = false;

   /* IODeviceController::Instance().GetIODevice(DeviceID::PCI2312A).
        BindKey(EKeys::Channel_02, IE_Repeat, this, &CIOMFCTestDlg::OnKeyAPressed);*/
    //PCI2312A.BindAction("Jump", IE_Pressed, this, &CIOMFCTestDlg::DynamicFunc, &a, &b);
    // TODO: 在此添加额外的初始化代码
    SetTimer(1, 0.02, NULL);

	return TRUE;  // 除非将焦点设置到控件，否则返回 TRUE
}

void CIOMFCTestDlg::OnSysCommand(UINT nID, LPARAM lParam)
{
	if ((nID & 0xFFF0) == IDM_ABOUTBOX)
	{
		CAboutDlg dlgAbout;
		dlgAbout.DoModal();
	}
	else
	{
		CDialogEx::OnSysCommand(nID, lParam);
	}
}

// 如果向对话框添加最小化按钮，则需要下面的代码
//  来绘制该图标。  对于使用文档/视图模型的 MFC 应用程序，
//  这将由框架自动完成。

void CIOMFCTestDlg::OnPaint()
{
	if (IsIconic())
	{
		CPaintDC dc(this); // 用于绘制的设备上下文

		SendMessage(WM_ICONERASEBKGND, reinterpret_cast<WPARAM>(dc.GetSafeHdc()), 0);

		// 使图标在工作区矩形中居中
		int cxIcon = GetSystemMetrics(SM_CXICON);
		int cyIcon = GetSystemMetrics(SM_CYICON);
		CRect rect;
		GetClientRect(&rect);
		int x = (rect.Width() - cxIcon + 1) / 2;
		int y = (rect.Height() - cyIcon + 1) / 2;

		// 绘制图标
		dc.DrawIcon(x, y, m_hIcon);
	}
	else
	{
		CDialogEx::OnPaint();
	}
}

//当用户拖动最小化窗口时系统调用此函数取得光标
//显示。
HCURSOR CIOMFCTestDlg::OnQueryDragIcon()
{
	return static_cast<HCURSOR>(m_hIcon);
}

void CIOMFCTestDlg::OnTimer(UINT_PTR nIDEvent)
{
    // TODO: 在此添加消息处理程序代码和/或调用默认值

    using namespace DevelopHelper;
    //OutputDebugStringA("Update...\n");
    IODeviceController::Instance().Update();

    IODevice& standardDevice = IODeviceController::Instance().GetIODevice("Standard");
    if (standardDevice.GetKeyDown("abc")) 
    {
        OutputDebugStringA("S down \n");
    }

    if (standardDevice.GetKeyUp(EKeys::F2))
    {
        OutputDebugStringA("S up \n");
    }

    float val = standardDevice.GetAxisKey("Axis_50");
   /* OutputDebugStringA(std::to_string(val).data());
    OutputDebugStringA("\n");
*/
   /* float duration = IODeviceController::Instance().GetIODevice("Standard").GetKeyDownDuration(EKeys::F2);
    int val = IODeviceController::Instance().GetIODevice("Standard").IsKeyPressed(EKeys::F2);
    OutputDebugStringA((std::to_string(val) +"---"+ std::to_string(duration)).data());
    OutputDebugStringA("\n");
*/
    CDialogEx::OnTimer(nIDEvent);
}

void CIOMFCTestDlg::DynamicFunc(int* a, bool* b)
{
    *a = *a + 1;
    int c = *a;
}

void CIOMFCTestDlg::OnMoveLR(float val)
{
    //OutputDebugStringA(std::string("Move LR ").append(std::to_string(val).append("\n")).c_str());
}

void CIOMFCTestDlg::OnMoveUD(float val)
{
    //OutputDebugStringA(std::string("Move UD ").append(std::to_string(val).append("\n")).c_str());
}

void CIOMFCTestDlg::OnMouseMove(float val)
{
    //OutputDebugStringA(std::to_string(val).append("\n").data());
}

void CIOMFCTestDlg::OnAction()
{
    OutputDebugStringA("Test action \n");
}

void CIOMFCTestDlg::OnActionWithKeyDown(DevelopHelper::FKey key)
{
   
    OutputDebugStringA(std::string(key.GetName()).append(" pressed sdf \r\n\n").data());
}

void CIOMFCTestDlg::OnActionWithKeyUp(DevelopHelper::FKey key)
{
    
    //OutputDebugStringA(std::string(key.GetName()).append(" released \r\n\n").data());
}

void CIOMFCTestDlg::OnActionWithKey3(DevelopHelper::FKey key)
{
   OutputDebugStringA("Jump \n");
}

void CIOMFCTestDlg::OnReleaseWithKey(DevelopHelper::FKey key)
{
    OutputDebugStringA(std::string(key.GetName()).append(" released \n").data());
}

void CIOMFCTestDlg::OnAxis(float val)
{
   //OutputDebugStringA(std::to_string(val).append("\n").data());
}

void CIOMFCTestDlg::MoveRight()
{
    OutputDebugStringA("Move Right \n");
}

void CIOMFCTestDlg::OnFire()
{

    OutputDebugStringA("Fire \n");
}

void CIOMFCTestDlg::Jump()
{
    OutputDebugStringA("Jump \n");
}

void CIOMFCTestDlg::OnRMPressed()
{
    DevelopHelper::IODeviceController::Instance().GetIODevice("Standard").SetDeviceDO("Button_100", 1);
}

void CIOMFCTestDlg::OnRMReleased()
{
    DevelopHelper::IODeviceController::Instance().GetIODevice("Standard").SetDeviceDO("Button_100", 0);
}

void CIOMFCTestDlg::OnKeyAPressed()
{
    OutputDebugStringA("Key Button_100 Pressed. \n");
}

void CIOMFCTestDlg::OnKeyBPressed()
{
    OutputDebugStringA("Key B Pressed. \n");
}

void CIOMFCTestDlg::OnClear()
{
    OutputDebugStringA("\n\n\n\n\r\n");
}

void CIOMFCTestDlg::OnLMDoubleClick()
{
    OutputDebugStringA("Left mouse button Double click\n");
}
