
// IOMFCTestDlg.cpp : 实现文件
//

#include "stdafx.h"
#include "IOMFCTest.h"
#include "IOMFCTestDlg.h"
#include "afxdialogex.h"

#include <chrono>
#include <ctime>

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
	, out_radio_group(0)
	, radio_read_oaction(0)
	, radio_input_value(0)
{
	m_hIcon = AfxGetApp()->LoadIcon(IDR_MAINFRAME);
}

void CIOMFCTestDlg::DoDataExchange(CDataExchange* pDX)
{
	CDialogEx::DoDataExchange(pDX);
	DDX_Control(pDX, IDC_SLIDER1, oaction_slider);
	DDX_Control(pDX, IDC_EDIT1, oaction_input);
	DDX_Radio(pDX, IDC_RADIO2, out_radio_group);
	DDX_Control(pDX, IDC_STATIC1, o_set_value);
	DDX_Control(pDX, IDC_STATIC2, real_output_text);
	DDX_Control(pDX, IDC_STATIC11, output_title);
	DDX_Control(pDX, IDC_STATIC12, input_title);
	DDX_Control(pDX, IDC_EDIT2, read_oaction_input);
	DDX_Radio(pDX, IDC_RADIO3, radio_read_oaction);
	DDX_Control(pDX, IDC_STATIC13, write_label);
	DDX_Radio(pDX, IDC_RADIO5, radio_input_value);
	DDX_Control(pDX, IDC_EDIT4, action_input);
	DDX_Control(pDX, IDC_STATIC3, read_axis_value);
	DDX_Control(pDX, IDC_STATIC14, read_label);
}


void CIOMFCTestDlg::ReBindActions()
{
	using namespace DevelopHelper;
	IODevice& extDev = IODeviceController::Instance().GetIODevice("ExtDev");
	extDev.BindAction("TestAction", IE_Pressed, this, &CIOMFCTestDlg::OnActionWithKeyDown);
	extDev.BindAction("TestAction", IE_Released, this, &CIOMFCTestDlg::OnActionWithKeyUp);
	extDev.BindAction("TestAction", IE_Repeat, this, &CIOMFCTestDlg::OnActionWithKeyRepeat);

	extDev.BindAxis("TestAxis", this, &CIOMFCTestDlg::OnAxis);
}

BEGIN_MESSAGE_MAP(CIOMFCTestDlg, CDialogEx)
	ON_WM_SYSCOMMAND()
	ON_WM_PAINT()
	ON_WM_QUERYDRAGICON()
    ON_WM_TIMER()
	ON_STN_CLICKED(label_btn_status, &CIOMFCTestDlg::OnStnClickedbtnstatus)
	ON_WM_HSCROLL()
	ON_BN_CLICKED(IDC_RADIO2, &CIOMFCTestDlg::OnBnClickedRadio2)
	ON_BN_CLICKED(IDC_RADIO1, &CIOMFCTestDlg::OnBnClickedRadio2)
//	ON_EN_CHANGE(IDC_EDIT2, &CIOMFCTestDlg::OnEnChangeEdit2)
	ON_BN_CLICKED(IDC_RADIO3, &CIOMFCTestDlg::OnBnClickedRadio3)
	ON_BN_CLICKED(IDC_RADIO4, &CIOMFCTestDlg::OnBnClickedRadio3)
	ON_BN_CLICKED(IDC_RADIO5, &CIOMFCTestDlg::OnBnClickedRadio5)
	ON_BN_CLICKED(IDC_RADIO6, &CIOMFCTestDlg::OnBnClickedRadio5)
	ON_BN_CLICKED(IDC_BUTTON2, &CIOMFCTestDlg::OnBnClickedButton2)
END_MESSAGE_MAP()

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

    /*IOSettings::Instance().SetIOConfigPath("IODevice.xml");*/
	IODeviceController::Instance().Load();
	IODevice& extDev = IODeviceController::Instance().GetIODevice("ExtDev");
   
	this->ReBindActions();

    // TODO: 在此添加额外的初始化代码
    SetTimer(1, 0.02, NULL);

	oaction_slider.SetRange(0, 1000);
	oaction_slider.SetTicFreq(10);
	action_input.SetWindowTextW(TEXT("Axis_00"));
	oaction_input.SetWindowTextW(TEXT("OAxis_00"));
	read_oaction_input.SetWindowTextW(TEXT("OAxis_00"));
	
	CFont _font;
	_font.CreateFont(24, 0, 0, 0, FW_NORMAL, FALSE, FALSE, 0, ANSI_CHARSET,OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, DEFAULT_QUALITY, DEFAULT_PITCH | FF_SWISS,TEXT("微软雅黑"));
	oaction_input.SetFont(&_font);
	
	read_oaction_input.SetFont(&_font);
	action_input.SetFont(&_font);

	CFont _titleFont;
	_titleFont.CreateFont(20, 0, 0, 0, FW_BOLD, TRUE, FALSE, 0, ANSI_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY, DEFAULT_PITCH | FF_SWISS, TEXT("Arial"));
	output_title.SetFont(&_titleFont);
	input_title.SetFont(&_titleFont);
	
	write_label.SetFont(&_titleFont);
	read_label.SetFont(&_titleFont);
	
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

	std::string _output = GetOInputStr(read_oaction_input);
	float _outputValue = 0;
	if (radio_read_oaction == 0) {
		_outputValue = IODeviceController::Instance().GetIODevice("ExtDev").GetDO(FKey(_output.data()));
	}
	else if (radio_read_oaction == 1) {
		_outputValue = IODeviceController::Instance().GetIODevice("ExtDev").GetDO(_output.data());
	}
	real_output_text.SetWindowTextW(std::to_wstring(_outputValue).data());

	std::string _axis = GetOInputStr(action_input);
	float _axisInputValue = 0;
	if (radio_input_value == 0) {
		_axisInputValue = IODeviceController::Instance().GetIODevice("ExtDev").GetAxisKey(FKey(_axis.data()));
	}
	else if (radio_input_value == 1) {
		_axisInputValue = IODeviceController::Instance().GetIODevice("ExtDev").GetAxis(_axis.data());
	}
	read_axis_value.SetWindowTextW(std::to_wstring(_axisInputValue).data());
    CDialogEx::OnTimer(nIDEvent);
}

void CIOMFCTestDlg::OnActionWithKeyDown(DevelopHelper::FKey key)
{
	using namespace DevelopHelper;
	std::string _msg = std::string(key.GetName()).append(" pressed\n");
	SetDlgItemTextA(this->m_hWnd, label_btn_status, _msg.data());
}

void CIOMFCTestDlg::OnActionWithKeyUp(DevelopHelper::FKey key)
{
	using namespace DevelopHelper;
	std::string _msg = std::string(key.GetName()).append(" released\n");
	SetDlgItemTextA(this->m_hWnd, label_btn_status, _msg.data());
}

void CIOMFCTestDlg::OnActionWithKeyRepeat(DevelopHelper::FKey key)
{
	//OutputDebugStringA(std::string(key.GetName()).append(" repeat\n").data());   
}


void CIOMFCTestDlg::OnAxis(float val)
{
	std::string _msg = std::to_string(val);
  // OutputDebugStringA(_msg.data());
   SetDlgItemTextA(this->m_hWnd, label_axis_status, _msg.data());
}

std::string CIOMFCTestDlg::GetOInputStr(const CEdit& _edit)
{
	CString _cstringVal;
	_edit.GetWindowTextW(_cstringVal);
	std::wstring _wstringVal(_cstringVal);
	return std::string(_wstringVal.begin(), _wstringVal.end());
}

void CIOMFCTestDlg::OnLMDoubleClick()
{
    OutputDebugStringA("Left mouse button Double click\n");
}


void CIOMFCTestDlg::OnStnClickedbtnstatus()
{
	// TODO: 在此添加控件通知处理程序代码
}


void CIOMFCTestDlg::OnHScroll(UINT nSBCode, UINT nPos, CScrollBar* pScrollBar)
{
	using namespace DevelopHelper;
	
	std::string _resultStr = this->GetOInputStr(oaction_input);
	int _sliderValue = oaction_slider.GetPos();
	o_set_value.SetWindowTextW(std::to_wstring(_sliderValue).data());
	switch (out_radio_group)
	{
	case 0:
		IODeviceController::Instance().GetIODevice("ExtDev").SetDO(FKey(_resultStr.data()), _sliderValue);
		break;
	case 1:
		IODeviceController::Instance().GetIODevice("ExtDev").SetDO(_resultStr.data(), _sliderValue);
		break;
	default:
		break;
	}

	// TODO: 在此添加消息处理程序代码和/或调用默认值
	CDialogEx::OnHScroll(nSBCode, nPos, pScrollBar);
}


void CIOMFCTestDlg::OnBnClickedRadio2()
{
	// TODO: 在此添加控件通知处理程序代码
	UpdateData(TRUE);
}


void CIOMFCTestDlg::AppendLog(std::wstring InMsg)
{

	auto now = std::chrono::system_clock::now();
	std::time_t start_time = std::chrono::system_clock::to_time_t(now);
	char timedisplay[100];
	struct tm buf;
	errno_t err = localtime_s(&buf, &start_time);

	std::strftime(timedisplay, sizeof(timedisplay), "%Y-%m-%d %H:%M:%S", &buf);
	std::string _time = timedisplay;
	std::wstring _wTime(_time.begin(), _time.end());

	CString _content;
	//log_view.GetWindowTextW(_content);
	std::wstring _contentStr(_content.GetString());
	std::wstring _log = _wTime + TEXT(":__________") + InMsg + TEXT("\r\n");
	_contentStr.append(_log);
	//log_view.SetWindowTextW(_contentStr.data());
}

void CIOMFCTestDlg::OnBnClickedRadio3()
{
	UpdateData(TRUE);
	// TODO: 在此添加控件通知处理程序代码
}


void CIOMFCTestDlg::OnBnClickedRadio5()
{
	UpdateData(TRUE);
	// TODO: 在此添加控件通知处理程序代码
}


void CIOMFCTestDlg::OnBnClickedButton2()
{
	DevelopHelper::IODeviceController::Instance().Unload();
	DevelopHelper::IODeviceController::Instance().Load();
	this->ReBindActions();
	AppendLog(TEXT("Hot reload succeed"));
	// TODO: 在此添加控件通知处理程序代码
}
