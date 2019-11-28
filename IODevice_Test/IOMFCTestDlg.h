
// IOMFCTestDlg.h : 头文件
//

#pragma once
#include "IODeviceController.h"

// CIOMFCTestDlg 对话框
class CIOMFCTestDlg : public CDialogEx
{
// 构造
public:
	CIOMFCTestDlg(CWnd* pParent = NULL);	// 标准构造函数

// 对话框数据
#ifdef AFX_DESIGN_TIME
	enum { IDD = IDD_IOMFCTEST_DIALOG };
#endif

	protected:
	virtual void DoDataExchange(CDataExchange* pDX);	// DDX/DDV 支持


// 实现
protected:
	HICON m_hIcon;

	// 生成的消息映射函数
	virtual BOOL OnInitDialog();
	afx_msg void OnSysCommand(UINT nID, LPARAM lParam);
	afx_msg void OnPaint();
	afx_msg HCURSOR OnQueryDragIcon();
	DECLARE_MESSAGE_MAP()
public:
    afx_msg void OnTimer(UINT_PTR nIDEvent);

    
    void DynamicFunc(int* a, bool* b);

    void OnMoveLR(float val);
    void OnMoveUD(float val);
    void OnMouseMove(float val);
    void OnAction();
    
    void OnActionWithKeyDown(DevelopHelper::FKey key);
    void OnActionWithKeyUp(DevelopHelper::FKey key);
    void OnActionWithKeyRepeat(DevelopHelper::FKey key);

    void OnReleaseWithKey(DevelopHelper::FKey key);
    void OnAxis(float val);
    void MoveRight();
    void OnFire();
    void Jump();
    void FunctionA();
    void FunctionB();


    void OnKeyAPressed();
    void OnKeyBPressed();
    void OnClear();

    void OnLMDoubleClick();
	afx_msg void OnStnClickedbtnstatus();
};
