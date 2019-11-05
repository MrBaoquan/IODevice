
// IOMFCTestDlg.cpp : ʵ���ļ�
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


// ����Ӧ�ó��򡰹��ڡ��˵���� CAboutDlg �Ի���

class CAboutDlg : public CDialogEx
{
public:
	CAboutDlg();

// �Ի�������
#ifdef AFX_DESIGN_TIME
	enum { IDD = IDD_ABOUTBOX };
#endif

	protected:
	virtual void DoDataExchange(CDataExchange* pDX);    // DDX/DDV ֧��

// ʵ��
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


// CIOMFCTestDlg �Ի���



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
	ON_STN_CLICKED(label_btn_status, &CIOMFCTestDlg::OnStnClickedbtnstatus)
END_MESSAGE_MAP()


void Func(DevelopHelper::BYTE arr[])
{
    int size_a = sizeof(arr);
    int size_b = sizeof(arr[0]);
    int result = size_a / size_b;
}

// CIOMFCTestDlg ��Ϣ�������

BOOL CIOMFCTestDlg::OnInitDialog()
{
	CDialogEx::OnInitDialog();

	// ��������...���˵�����ӵ�ϵͳ�˵��С�

	// IDM_ABOUTBOX ������ϵͳ���Χ�ڡ�
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

	// ���ô˶Ի����ͼ�ꡣ  ��Ӧ�ó��������ڲ��ǶԻ���ʱ����ܽ��Զ�
	//  ִ�д˲���
	SetIcon(m_hIcon, TRUE);			// ���ô�ͼ��
	SetIcon(m_hIcon, FALSE);		// ����Сͼ��

    using namespace DevelopHelper;
    

    DevelopHelper::BYTE arr[20];

    Func(arr);

   // int length = sizeof(arr) / sizeof(arr[0]);


    /*IOSettings::Instance().SetIOConfigPath("IODevice.xml");*/
	IODeviceController::Instance().Load();
	IODevice& extDev = IODeviceController::Instance().GetIODevice("ExtDev");
   
	//IODeviceController::Instance().GetIODevice("Standard").BindKey(EKeys::A, IE_Pressed, this, &CIOMFCTestDlg::FunctionA);
    //standardDevice.BindKey(EKeys::B, IE_Pressed, this, &CIOMFCTestDlg::OnRMReleased);
    extDev.BindAction("TestAction", IE_Pressed, this, &CIOMFCTestDlg::OnActionWithKeyDown);
	extDev.BindAction("TestAction", IE_Released, this, &CIOMFCTestDlg::OnActionWithKeyUp);

    //standardDevice.BindAxisKey("Axis_50", this, &CIOMFCTestDlg::OnAxis);

   // standardDevice.BindAction("Action2", IE_Pressed, this, &CIOMFCTestDlg::OnActionWithKey2);
    //standardDevice.BindAction("Action3", IE_Pressed, this, &CIOMFCTestDlg::OnActionWithKey3);
    //standardDevice.BindAction("AnyKey", IE_Released, this, &CIOMFCTestDlg::OnReleaseWithKey);
 
    extDev.BindAxis("TestAxis", this, &CIOMFCTestDlg::OnAxis);

    static int a = 1;
    static bool b = false;

   /* IODeviceController::Instance().GetIODevice(DeviceID::PCI2312A).
        BindKey(EKeys::Channel_02, IE_Repeat, this, &CIOMFCTestDlg::OnKeyAPressed);*/
    //PCI2312A.BindAction("Jump", IE_Pressed, this, &CIOMFCTestDlg::DynamicFunc, &a, &b);
    // TODO: �ڴ���Ӷ���ĳ�ʼ������
    SetTimer(1, 0.02, NULL);


	unsigned long _min = 0;
	unsigned long _max = ULONG_MAX-2;
	short _delta = _max - _min;
	long _long = _max;
	unsigned short _xx = _max;
	unsigned short _shortValueMin = static_cast<unsigned short>((static_cast<float>(_min) / ULONG_MAX) * USHORT_MAX);
	unsigned short _shortValueMax = static_cast<unsigned short>((static_cast<double>(100000.0) / ULONG_MAX) * USHORT_MAX);


	return TRUE;  // ���ǽ��������õ��ؼ������򷵻� TRUE
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

// �����Ի��������С����ť������Ҫ����Ĵ���
//  �����Ƹ�ͼ�ꡣ  ����ʹ���ĵ�/��ͼģ�͵� MFC Ӧ�ó���
//  �⽫�ɿ���Զ���ɡ�

void CIOMFCTestDlg::OnPaint()
{
	if (IsIconic())
	{
		CPaintDC dc(this); // ���ڻ��Ƶ��豸������

		SendMessage(WM_ICONERASEBKGND, reinterpret_cast<WPARAM>(dc.GetSafeHdc()), 0);

		// ʹͼ���ڹ����������о���
		int cxIcon = GetSystemMetrics(SM_CXICON);
		int cyIcon = GetSystemMetrics(SM_CYICON);
		CRect rect;
		GetClientRect(&rect);
		int x = (rect.Width() - cxIcon + 1) / 2;
		int y = (rect.Height() - cyIcon + 1) / 2;

		// ����ͼ��
		dc.DrawIcon(x, y, m_hIcon);
	}
	else
	{
		CDialogEx::OnPaint();
	}
}

//���û��϶���С������ʱϵͳ���ô˺���ȡ�ù��
//��ʾ��
HCURSOR CIOMFCTestDlg::OnQueryDragIcon()
{
	return static_cast<HCURSOR>(m_hIcon);
}

void CIOMFCTestDlg::OnTimer(UINT_PTR nIDEvent)
{
    // TODO: �ڴ������Ϣ�����������/�����Ĭ��ֵ

    using namespace DevelopHelper;
    //OutputDebugStringA("Update...\n");
    IODeviceController::Instance().Update();

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
	std::string _msg = std::string(key.GetName()).append(" pressed");
    OutputDebugStringA(_msg.data());
	SetDlgItemTextA(this->m_hWnd, label_btn_status, _msg.data());
}

void CIOMFCTestDlg::OnActionWithKeyUp(DevelopHelper::FKey key)
{
	std::string _msg = std::string(key.GetName()).append(" released");
	OutputDebugStringA(_msg.data());
	SetDlgItemTextA(this->m_hWnd, label_btn_status, _msg.data());
}

void CIOMFCTestDlg::OnActionWithKey3(DevelopHelper::FKey key)
{
   OutputDebugStringA( key.GetName());   
   OutputDebugStringA("\n");
}

void CIOMFCTestDlg::OnReleaseWithKey(DevelopHelper::FKey key)
{
    OutputDebugStringA(std::string(key.GetName()).append(" released \n").data());
}

void CIOMFCTestDlg::OnAxis(float val)
{
	std::string _msg = std::to_string(val);
   OutputDebugStringA(_msg.data());
   SetDlgItemTextA(this->m_hWnd, label_axis_status, _msg.data());
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

void CIOMFCTestDlg::FunctionA()
{
	using namespace  DevelopHelper;
	IODeviceController::Instance().Unload();
	IODeviceController::Instance().Load();
	IODeviceController::Instance().GetIODevice("Standard").BindKey(EKeys::A, IE_Pressed, this, &CIOMFCTestDlg::FunctionB);
	OutputDebugStringA("FunctionA");
}

void CIOMFCTestDlg::FunctionB()
{
	using namespace  DevelopHelper;
	IODeviceController::Instance().Unload();
	IODeviceController::Instance().Load();
	IODeviceController::Instance().GetIODevice("Standard").BindKey(EKeys::A, IE_Pressed, this, &CIOMFCTestDlg::FunctionA);
	OutputDebugStringA("FunctionB");
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


void CIOMFCTestDlg::OnStnClickedbtnstatus()
{
	// TODO: �ڴ���ӿؼ�֪ͨ����������
}
