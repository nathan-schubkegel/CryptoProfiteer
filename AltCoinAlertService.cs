using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CryptoProfiteer
{
  public interface IAltCoinAlertService
  {
    IReadOnlyList<AltCoinAlert> Alerts { get; }
  }

  public class AltCoinAlertService : IAltCoinAlertService
  {
    private readonly object _lock = new object();
    private readonly List<AltCoinAlert> _alerts = new List<AltCoinAlert>();
    
    public IReadOnlyList<AltCoinAlert> Alerts => _alerts;

    public AltCoinAlertService()
    {
      // copy pasted from some excel spreadsheet I built
      var lineData = @"
Date	Hype					Gain					Loss
1/1/2022	KP3R	TOMO	MIOTA	LTO	FTM	KP3R	TOP	SNTVT	UBT	SCRT	EGG	FUN	ORN	CEL	SUSHI
1/2/2022	KP3R	TOMO	MIOTA	LTO	FTM	ELF	XRT	POLY	MFT	HNS	SXP	CHR	YUSRA	AR	XPRT
1/3/2022	SNTVT	SCRT	XYO	AMPL	FTM	FTM	NEAR	SCRT	HTR	SNX	YUSRA	ELF	POLY	UBT	FSN
1/4/2022	FTM	SNTVT	DCR	ALPHA	RUNE	ICP	DAG	RVN	SBR	XTZ	KP3R	AMP	HNS	PRE	MIOTA
1/5/2022	AXS	SOL	LUNA	HNT	GALA	UMA	CDT	TOKE	MOVR	TOP	CEL	SBR	ICP	CHR	AXS
1/6/2022	SOL	LUNA	DAG	CHR	NKN	KP3R	TOKE	MANA	PRE	ONE	XHV	MIR	SNTVT	MOVR	SLP
1/7/2022	AXS	PERP	ELF	RVN	MATIC	IRIS	NEW	CREAM	KNC	KAVA	NLG	DFI	EGG	XPRT	COTI
1/8/2022	COTI	EFX	ELF	LUNA	NLG	ICP	TOP	SKY	KAVA	CDT	IRIS	TOKE	SCRT	NLG	AKT
1/9/2022	KAVA	RUNE	PERP	SOL	DAG	STEEM	EFX	NLG	CDT	CREAM	ONE	GNO	KAVA	KNC	SKY
1/10/2022	RVN	COTI	TOP	CELR	NKN	NEAR	NEW	UTK	KP3R	REP	SBR	SNTVT	EFX	STEEM	YGG
1/11/2022	KOIN	KAVA	STMX	VIDT	RING	NEW	AST	AMPL	SBR	FTM	ICP	DASH	LINK	STEEM	NOTA
1/12/2022	DODO	KP3R	AXS	GNO	STORJ	CREAM	ETN	KP3R	HNS	TOP	SBR	NEAR	MINA	AST	REP
1/13/2022	AMPL	KOIN	ELF	ONE	XTZ	NEW	SCRT	FUN	DOGE	CREAM	YUSRA	HNS	HTR	SNTVT	DAG
1/14/2022	DAG	SCRT	CHZ	XTZ	MLN	NEW	DCR	ELA	FSN	ROSE	SNT	DAPP	CEL	LRC	HTR
1/16/2022	AXS	CELR	SCRT	FTM	CHZ	SBR	MIR	RLY	SDN	TOP	AE	CREAM	KOTN	CELR	ROSE
1/17/2022	PRQ	YFI	ONE	AE	VIDT	ELA	COTI	AMPL	EASY	REQ	BTT	CREAM	SBR	SDN	PRQ
1/18/2022	KSDM	ONE	NEAR	DAPP	GNO	STX	CREAM	XCM	ETC	THETA	AMPL	EASY	KAVA	TRU	REQ
1/19/2022	BEAM	SUSHI	CELR	AKT	LRC	CDT	ZAP	ELA	FTT	KNC	TOP	CREAM	STX	MIR	SNTVT
1/20/2022	AXS	UTK	HTR	GAME	XOR	YUSRA	ZAP	CDT	PERP	DAPP	NEW	SCRT	OGN	NLG	YGG
1/21/2022	BEAM	EFX	AKT	ELA	BAND	YUSRA	TOP	CDT	MKR	MIR	SNTVT	KOIN	SLP	XCM	XHV
1/23/2022	LPT	ERG	OMG	XRT	RSR	ELF	SHR	FUN	ELA	ATOM	YUSRA	TOP	NEW	CELR	DYDX
1/24/2022	AMP	COTI	TRAC	PERP	DAG	TOP	KNC	AE	MAN	SNX	PERP	CREAM	YUSRA	ELA	ELF
1/25/2022	LRC	SXP	BAL	1INCH	TRX	XCM	YUSRA	LRC	ELA	FX	MAN	XOR	MKR	TOP	ATOM
1/26/2022	LRC	GNO	TOMO	YFII	SWAP	TFUEL	MIR	FX	XCM	WAVES	CREAM	ELA	AE	AR	ATOM
1/27/2022	HTR	SAND	KEEP	LOOM	LRC	NMR	XOR	COTI	SAND	ORN	TOKE	SLP	XCM	HTR	UBT
1/28/2022	LUNA	HTR	STEEM	AXS	ELA	NU	NEW	SDN	UBT	SNTVT	TOP	NMR	MIR	NLG	GAME
1/29/2022	AXS	ICP	REP	AMP	STMX	FLOW	SNX	NEW	BEAM	SAND	HTR	KP3R	KNC	NMR	BTT
1/30/2022	KCS	MIR	SKL	MLN	DOT	ANKR	SKY	CDT	RING	HXRO	ELA	KDA	LUNA	YFII	UBT
1/31/2022	ORN	DAPP	OCEAN	SNTVT	SAND	HXRO	CVC	CREAM	LUNA	ELA	ZAP	XCM	TOP	EFX	NOIA
2/1/2022	HIVE	SAND	XHV	BLZ	GNO	WRX	LEO	YGG	PRQ	TOP	ORN	NOIA	CVC	LRC	DAG
2/2/2022	QNT	LRC	KEEP	LEO	VIDT	HIVE	REP	QNT	VIDT	STEEM	AR	UBT	SOL	HTR	MASK
2/3/2022	ANT	FLOW	CRV	SOL	AR	SLP	QNT	ATOM	XCM	IRIS	HIVE	RING	LPT	AST	PRQ
2/4/2022	KP3R	LUNA	AMPL	XYO	ELF	FSN	BLZ	LEO	AMPL	NMR	SLP	MIR	YUSRA	MKR	XCM
2/5/2022	PERP	LRC	SAND	EASY	DODO	GALA	CFX	TFUEL	XHV	TOP	LEO	MAN	UBT	POLS	NEW
2/6/2022	GALA	AXS	LEO	NKN	SCRT	SHIB	SLP	AXS	LRC	DFI	AMPL	GALA	CFX	ZAP	TOP
2/7/2022	AXS	OKB	GALA	DAPP	HIVE	SLP	KDA	XRP	XNT	MATIC	LEO	AXS	TOP	TFUEL	YUSRA
2/8/2022	TOP	XYO	LRC	LEO	MINA	LEO	IOTX	CDT	RING	AION	MAN	HTR	DYDX	SLP	AR
2/9/2022	DGB	UBT	TFUEL	VTHO	SOL	SLP	KEEP	VTHO	TOKE	NU	LEO	TOP	KOIN	MIOTA	ZEC
2/10/2022	CAKE	AR	KAVA	VET	NEXO	THETA	SLP	TRU	BEAM	CDT	ZAP	PRQ	KDA	XHV	NLG
2/11/2022	TRAC	NEXO	STMX	XEM	HTR	INJ	PHA	BAND	CDT	IRIS	XLP	KDA	CFX	LPT	UBT
2/12/2022	LEO	TEL	DAPP	MAN	TFUEL	TOP	SLP	CDT	CFX	XRP	INJ	IOTX	SKL	XTZ	LPT
2/13/2022	KP3R	COTI	DNT	ONE	VIDT	CDT	SLP	OXT	DAPP	EFX	INJ	PHA	BLZ	HNT	SNX

";
      foreach (var line in lineData.GetLines().Where(x => x.Trim().Length > 0).Skip(1))
      {
        var fields = line.Split('\t');
        var time = DateTime.Parse(fields[0] + " 11:30:00 PM", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal).ToUniversalTime();
        var hypes = fields.Skip(1).Take(5).ToList();
        var winners = fields.Skip(6).Take(5).ToList();
        var losers = fields.Skip(11).Take(5).ToList();
        _alerts.Add(new AltCoinAlert(time, hypes, winners, losers));
      }
    }
  }
}