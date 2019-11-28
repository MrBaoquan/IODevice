
// IOMFCTestDlg.h : ͷ�ļ�
//

#pragma once
#include "IODeviceController.h"

// CIOMFCTestDlg �Ի���
class CIOMFCTestDlg : public CDialogEx
{
// ����
public:
	CIOMFCTestDlg(CWnd* pParent = NULL);	// ��׼���캯��

// �Ի�������
#ifdef AFX_DESIGN_TIME
	enum { IDD = IDD_IOMFCTEST_DIALOG };
#endif

	protected:
	virtual void DoDataExchange(CDataExchange* pDX);	// DDX/DDV ֧��


// ʵ��
protected:
	HICON m_hIcon;

	// ���ɵ���Ϣӳ�亯��
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
