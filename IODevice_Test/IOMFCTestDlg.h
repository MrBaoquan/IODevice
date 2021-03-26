
// IOMFCTestDlg.h : ͷ�ļ�
//

#pragma once
#include "IODeviceController.h"
#include "afxcmn.h"
#include "afxwin.h"

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

	void ReBindActions();
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
    
    void OnActionWithKeyDown(DevelopHelper::FKey key);
    void OnActionWithKeyUp(DevelopHelper::FKey key);
    void OnActionWithKeyRepeat(DevelopHelper::FKey key);
	void OnAxis(float InValue);

	std::string GetOInputStr(const CEdit& _edit);
    void OnLMDoubleClick();
	afx_msg void OnStnClickedbtnstatus();
private:
	CSliderCtrl oaction_slider;
public:
	afx_msg void OnHScroll(UINT nSBCode, UINT nPos, CScrollBar* pScrollBar);
	CEdit oaction_input;
	int out_radio_group;
	afx_msg void OnBnClickedRadio2();
	

	void AppendLog(std::wstring InMsg);

	CStatic o_set_value;
	CStatic real_output_text;
	CStatic output_title;
	CStatic input_title;
//	afx_msg void OnEnChangeEdit2();
	CEdit read_oaction_input;
	int radio_read_oaction;
	afx_msg void OnBnClickedRadio3();
	CStatic write_label;
	int radio_input_value;
	afx_msg void OnBnClickedRadio5();
	CEdit action_input;
	CStatic read_axis_value;
	CStatic read_label;
	afx_msg void OnBnClickedButton2();
	afx_msg void OnBnClickedButton3();
	afx_msg void OnBnClickedButton1();

	void SyncDO(float newValue);
	CButton btn_plus;
	CButton btn_minus;
};
