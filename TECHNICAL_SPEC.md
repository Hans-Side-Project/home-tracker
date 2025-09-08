# 房貸試算器 - 技術實作規格文件

## 技術架構概述

### 核心技術棧

- **前端框架**: Blazor Server (.NET 8)
- **後端**: ASP.NET Core 8
- **UI 框架**: Bootstrap 5 + Bootstrap Icons
- **圖表庫**: Chart.js 或 ApexCharts
- **部署平台**: 待定 (建議 Azure App Service 或 Docker)
- **資料庫**: 暫不需要 (純計算功能)

### 專案結構

```text
HouseTrackerApp/
├── Components/
│   ├── Layout/
│   ├── Pages/
│   ├── Shared/
│   │   └── IconInput.razor
│   ├── LoanInputForm.razor
│   ├── ResultDisplay.razor
│   └── Charts/
├── Services/
│   ├── ILoanCalculationService.cs
│   ├── LoanCalculationService.cs
│   └── IValidationService.cs
├── Models/
│   ├── LoanCalculationInput.cs
│   ├── LoanCalculationResult.cs
│   └── ValidationModels.cs
├── Helpers/
│   └── LoanValidationHelper.cs
└── wwwroot/
    ├── css/
    └── js/
```

## 核心計算邏輯實作

### 房貸計算公式

#### 本息攤還公式

```text
月付金 = 貸款本金 × [月利率 × (1 + 月利率)^還款月數] / [(1 + 月利率)^還款月數 - 1]
```

```csharp
public decimal CalculateEqualInstallment(decimal principal, decimal monthlyRate, int periods)
{
    if (monthlyRate == 0) return principal / periods;
    
    var factor = Math.Pow((double)(1 + monthlyRate), periods);
    return principal * monthlyRate * (decimal)factor / ((decimal)factor - 1);
}
```

#### 本金攤還公式

```text
每月固定本金 = 貸款本金 ÷ 還款月數
每月利息 = 剩餘本金 × 月利率
每月還款金額 = 每月固定本金 + 每月利息
```

```csharp
public List<MonthlyPaymentDetail> CalculateEqualPrincipal(decimal principal, decimal monthlyRate, int periods)
{
    var monthlyPrincipal = principal / periods;
    var schedule = new List<MonthlyPaymentDetail>();
    var remainingBalance = principal;
    
    for (int period = 1; period <= periods; period++)
    {
        var monthlyInterest = remainingBalance * monthlyRate;
        var monthlyPayment = monthlyPrincipal + monthlyInterest;
        
        schedule.Add(new MonthlyPaymentDetail
        {
            Period = period,
            Principal = monthlyPrincipal,
            Interest = monthlyInterest,
            Payment = monthlyPayment,
            RemainingBalance = remainingBalance - monthlyPrincipal
        });
        
        remainingBalance -= monthlyPrincipal;
    }
    
    return schedule;
}
```

#### 寬限期計算邏輯

```csharp
public LoanCalculationResult CalculateWithGracePeriod(LoanCalculationInput input)
{
    var monthlyRate = input.AnnualInterestRate / 12;
    var gracePeriodPayment = input.LoanAmount * monthlyRate;
    var gracePeriodInterest = gracePeriodPayment * input.GracePeriodMonths;
    
    // 計算寬限期後的還款期數
    var postGracePeriods = input.IsGracePeriodIncludedInTerm
        ? (input.LoanTermYears * 12) - input.GracePeriodMonths
        : input.LoanTermYears * 12;
    
    var postGracePeriodPayment = CalculateEqualInstallment(
        input.LoanAmount, monthlyRate, postGracePeriods);
    
    return new LoanCalculationResult
    {
        GracePeriodInfo = new GracePeriodDetail
        {
            GracePeriodMonths = input.GracePeriodMonths,
            GracePeriodMonthlyPayment = gracePeriodPayment,
            GracePeriodTotalInterest = gracePeriodInterest,
            PostGracePeriodMonthlyPayment = postGracePeriodPayment,
            PaymentIncrease = postGracePeriodPayment - gracePeriodPayment,
            PaymentIncreaseRatio = (postGracePeriodPayment - gracePeriodPayment) / gracePeriodPayment
        }
    };
}
```

#### 階段式利率計算邏輯

```csharp
public LoanCalculationResult CalculateStagedRate(LoanCalculationInput input)
{
    var schedule = new List<MonthlyPaymentDetail>();
    var totalMonths = input.LoanTermYears * 12;
    var stages = input.StagedRateManager.Stages;
    
    var remainingBalance = input.EffectiveLoanAmount;
    var totalInterest = 0m;
    
    var currentPeriod = 1;
    
    // 遍歷每個利率階段
    foreach (var stage in stages)
    {
        var stageMonths = stage.Years * 12;
        var monthlyRate = stage.MonthlyRate;
        
        // 計算此階段剩餘的還款期數
        var remainingTotalMonths = totalMonths - (currentPeriod - 1);
        var stageMonthlyPayment = CalculateEqualInstallment(
            remainingBalance, monthlyRate, remainingTotalMonths);
        
        // 處理此階段的每月還款
        for (int monthInStage = 1; monthInStage <= stageMonths && currentPeriod <= totalMonths; monthInStage++)
        {
            var monthlyInterest = remainingBalance * monthlyRate;
            var monthlyPrincipal = stageMonthlyPayment - monthlyInterest;
            
            schedule.Add(new MonthlyPaymentDetail
            {
                Period = currentPeriod,
                PaymentDate = DateTime.Today.AddMonths(currentPeriod),
                Payment = stageMonthlyPayment,
                Principal = monthlyPrincipal,
                Interest = monthlyInterest,
                RemainingBalance = remainingBalance - monthlyPrincipal,
                IsGracePeriod = false,
                StageNumber = stage.StageNumber,
                StageRate = stage.AnnualRate
            });
            
            remainingBalance -= monthlyPrincipal;
            totalInterest += monthlyInterest;
            currentPeriod++;
        }
    }
    
    return new LoanCalculationResult
    {
        MonthlyPayment = schedule.FirstOrDefault()?.Payment ?? 0, // 第一階段月付金
        TotalInterest = totalInterest,
        TotalPayment = input.EffectiveLoanAmount + totalInterest,
        PaymentSchedule = schedule,
        IsCalculationStable = true,
        StagedCalculationSummary = CreateStagedCalculationSummary(stages, schedule)
    };
}

/// <summary>
/// 建立階段式計算摘要
/// </summary>
private StagedCalculationSummary CreateStagedCalculationSummary(
    List<InterestRateStage> stages, 
    List<MonthlyPaymentDetail> schedule)
{
    var stageSummaries = new List<StageSummary>();
    
    foreach (var stage in stages)
    {
        var stagePayments = schedule.Where(p => p.StageNumber == stage.StageNumber).ToList();
        if (stagePayments.Any())
        {
            stageSummaries.Add(new StageSummary
            {
                StageNumber = stage.StageNumber,
                StageRate = stage.AnnualRate,
                StageYears = stage.Years,
                StageMonthlyPayment = stagePayments.First().Payment,
                StageTotalInterest = stagePayments.Sum(p => p.Interest),
                StageTotalPayment = stagePayments.Sum(p => p.Payment),
                StartPeriod = stagePayments.Min(p => p.Period),
                EndPeriod = stagePayments.Max(p => p.Period)
            });
        }
    }
    
    return new StagedCalculationSummary
    {
        StageSummaries = stageSummaries,
        WeightedAverageRate = stages.Sum(s => s.AnnualRate * s.Years) / stages.Sum(s => s.Years),
        TotalStages = stages.Count
    };
}

public decimal CalculateStagedRateAveragePayment(LoanCalculationInput input)
{
    var stages = input.StagedRateManager.Stages;
    if (!stages.Any()) return 0;
    
    // 簡化計算：使用加權平均利率計算近似值
    var averageRate = input.StagedRateManager.WeightedAverageMonthlyRate;
    var totalMonths = input.LoanTermYears * 12;
    
    return CalculateEqualInstallment(input.EffectiveLoanAmount, averageRate, totalMonths);
}
```

## 資料模型設計

### 列舉型別定義

```csharp
/// <summary>
/// 貸款類型
/// </summary>
public enum LoanType
{
    [Display(Name = "本息攤還")]
    EqualInstallment = 1,

    [Display(Name = "本金攤還")]
    EqualPrincipal = 2,

    [Display(Name = "只繳利息")]
    InterestOnly = 3
}

/// <summary>
/// 驗證訊息類型
/// </summary>
public enum ValidationMessageType
{
    Error = 1,    // 錯誤 (阻止計算)
    Warning = 2,  // 警告 (可執行但有風險)
    Info = 3      // 提醒 (資訊性提示)
}

/// <summary>
/// 貸款條件輸入模式
/// </summary>
public enum LoanInputMode
{
    [Display(Name = "以貸款成數為主")]
    LoanToValueRatio = 1,

    [Display(Name = "以貸款金額為主")]
    LoanAmount = 2
}

/// <summary>
/// 利率輸入模式
/// </summary>
public enum InterestRateMode
{
    [Display(Name = "固定利率")]
    Fixed = 1,

    [Display(Name = "階段式利率")]
    Staged = 2
}
```

### 階段式利率資料模型

```csharp
/// <summary>
/// 利率階段資料模型
/// </summary>
public class InterestRateStage
{
    [Required(ErrorMessage = "階段年利率為必填")]
    [Range(0.001, 0.2, ErrorMessage = "年利率須介於0.1%到20%之間")]
    [DisplayName("階段年利率")]
    [DisplayFormat(DataFormatString = "{0:P3}")]
    public decimal AnnualRate { get; set; } = 0.025m;

    [Required(ErrorMessage = "階段年限為必填")]
    [Range(1, 40, ErrorMessage = "階段年限須介於1到40年之間")]
    [DisplayName("階段年限")]
    public int Years { get; set; } = 1;

    [DisplayName("階段序號")]
    public int StageNumber { get; set; }

    [DisplayName("累計年數")]
    public int CumulativeYears => StageNumber == 1 ? Years : 0; // 由計算邏輯設定

    [DisplayName("階段月利率")]
    [DisplayFormat(DataFormatString = "{0:P3}")]
    public decimal MonthlyRate => AnnualRate / 12;
}

/// <summary>
/// 階段式利率管理器
/// </summary>
public class StagedInterestRateManager
{
    private List<InterestRateStage> _stages;
    private int _loanTermYears;

    public StagedInterestRateManager(int loanTermYears)
    {
        _loanTermYears = loanTermYears;
        _stages = new List<InterestRateStage>();
        
        // 預設新增一個階段
        AddStage();
    }

    [DisplayName("利率階段列表")]
    public List<InterestRateStage> Stages => _stages.ToList();

    [DisplayName("階段總年數")]
    public int TotalYears => _stages.Sum(s => s.Years);

    [DisplayName("年數是否匹配")]
    public bool IsYearsMatched => TotalYears == _loanTermYears;

    [DisplayName("加權平均年利率")]
    [DisplayFormat(DataFormatString = "{0:P3}")]
    public decimal WeightedAverageAnnualRate => _stages.Any() 
        ? _stages.Sum(s => s.AnnualRate * s.Years) / Math.Max(1, TotalYears)
        : 0;

    [DisplayName("加權平均月利率")]
    [DisplayFormat(DataFormatString = "{0:P3}")]
    public decimal WeightedAverageMonthlyRate => WeightedAverageAnnualRate / 12;

    /// <summary>
    /// 新增階段
    /// </summary>
    public bool AddStage()
    {
        if (_stages.Count >= 10) return false; // 最多10個階段
        
        var newStage = new InterestRateStage
        {
            StageNumber = _stages.Count + 1,
            Years = Math.Max(1, _loanTermYears - TotalYears) // 自動計算剩餘年數
        };
        
        _stages.Add(newStage);
        UpdateStageNumbers();
        return true;
    }

    /// <summary>
    /// 刪除階段
    /// </summary>
    public bool RemoveStage(int stageNumber)
    {
        if (_stages.Count <= 1) return false; // 最少保留一個階段
        
        var stageToRemove = _stages.FirstOrDefault(s => s.StageNumber == stageNumber);
        if (stageToRemove != null)
        {
            _stages.Remove(stageToRemove);
            UpdateStageNumbers();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 更新階段編號
    /// </summary>
    private void UpdateStageNumbers()
    {
        for (int i = 0; i < _stages.Count; i++)
        {
            _stages[i].StageNumber = i + 1;
        }
    }

    /// <summary>
    /// 驗證所有階段
    /// </summary>
    public List<string> ValidateStages()
    {
        var errors = new List<string>();
        
        if (!_stages.Any())
        {
            errors.Add("至少需要設定一個利率階段");
            return errors;
        }

        if (TotalYears != _loanTermYears)
        {
            errors.Add($"所有階段年限總和({TotalYears}年)必須等於貸款年限({_loanTermYears}年)");
        }

        for (int i = 0; i < _stages.Count; i++)
        {
            var stage = _stages[i];
            if (stage.Years <= 0)
            {
                errors.Add($"第{stage.StageNumber}階段年限必須大於0");
            }
            if (stage.AnnualRate <= 0 || stage.AnnualRate > 0.2m)
            {
                errors.Add($"第{stage.StageNumber}階段年利率必須介於0.1%到20%之間");
            }
        }

        return errors;
    }

    /// <summary>
    /// 自動調整階段年限以符合貸款年限
    /// </summary>
    public void AutoAdjustToMatchLoanTerm()
    {
        if (!_stages.Any()) return;

        var difference = _loanTermYears - TotalYears;
        if (difference == 0) return;

        // 將差額加到最後一個階段
        var lastStage = _stages.Last();
        lastStage.Years = Math.Max(1, lastStage.Years + difference);
    }
}
```

### 主要資料模型

```csharp
public class LoanCalculationInput
{
    [Range(1000000, 100000000, ErrorMessage = "房屋總價須介於100萬到1億之間")]
    [DisplayName("房屋總價")]
    public decimal HousePrice { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "自備款不可為負數")]
    [DisplayName("自備款")]
    public decimal DownPayment { get; set; }

    // 智能切換模式控制
    [Required(ErrorMessage = "請選擇輸入模式")]
    [DisplayName("輸入模式")]
    public LoanInputMode InputMode { get; set; } = LoanInputMode.LoanToValueRatio;

    [Range(100000, 80000000, ErrorMessage = "貸款金額須介於10萬到8000萬之間")]
    [DisplayName("貸款金額")]
    public decimal LoanAmount { get; set; }

    [Range(0.1, 0.9, ErrorMessage = "貸款成數須介於10%到90%之間")]
    [DisplayName("貸款成數")]
    public decimal LoanToValueRatio { get; set; }

    // 計算屬性：根據輸入模式自動計算非編輯欄位
    [DisplayName("計算得出的貸款金額")]
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal CalculatedLoanAmount => InputMode == LoanInputMode.LoanToValueRatio 
        ? (HousePrice - DownPayment) * LoanToValueRatio 
        : LoanAmount;

    [DisplayName("計算得出的貸款成數")]
    [DisplayFormat(DataFormatString = "{0:P1}")]
    public decimal CalculatedLoanToValueRatio => InputMode == LoanInputMode.LoanAmount 
        ? (HousePrice - DownPayment) > 0 ? LoanAmount / (HousePrice - DownPayment) : 0 
        : LoanToValueRatio;

    // 獲取有效的貸款金額 (無論哪種模式)
    [DisplayName("有效貸款金額")]
    public decimal EffectiveLoanAmount => InputMode == LoanInputMode.LoanToValueRatio 
        ? CalculatedLoanAmount 
        : LoanAmount;

    [Range(1, 40, ErrorMessage = "貸款年限須介於1年到40年之間")]
    [DisplayName("貸款年限")]
    public int LoanTermYears { get; set; }

    // 利率模式智能切換控制
    [Required(ErrorMessage = "請選擇利率模式")]
    [DisplayName("利率模式")]
    public InterestRateMode RateMode { get; set; } = InterestRateMode.Fixed;

    // 固定利率相關屬性
    [Range(0.001, 0.2, ErrorMessage = "年利率須介於0.1%到20%之間")]
    [DisplayName("固定年利率")]
    [DisplayFormat(DataFormatString = "{0:P3}")]
    public decimal FixedAnnualRate { get; set; } = 0.025m;

    // 階段式利率相關屬性
    private StagedInterestRateManager _stagedRateManager;
    
    [DisplayName("階段式利率管理器")]
    public StagedInterestRateManager StagedRateManager 
    { 
        get => _stagedRateManager ??= new StagedInterestRateManager(LoanTermYears);
        set => _stagedRateManager = value;
    }

    // 計算屬性：根據利率模式自動計算有效利率
    [DisplayName("有效年利率")]
    [DisplayFormat(DataFormatString = "{0:P3}")]
    public decimal EffectiveAnnualRate => RateMode == InterestRateMode.Fixed 
        ? FixedAnnualRate 
        : StagedRateManager.WeightedAverageAnnualRate;

    [DisplayName("有效月利率")]
    [DisplayFormat(DataFormatString = "{0:P3}")]
    public decimal EffectiveMonthlyRate => EffectiveAnnualRate / 12;

    // 更新階段式利率管理器的貸款年限
    public void UpdateStagedRateManagerLoanTerm()
    {
        if (_stagedRateManager != null)
        {
            _stagedRateManager = new StagedInterestRateManager(LoanTermYears);
        }
    }

    [Range(0, 60, ErrorMessage = "寬限期須介於0到5年之間")]
    [DisplayName("寬限期月數")]
    public int GracePeriodMonths { get; set; }

    [DisplayName("寬限期是否包含在貸款年限內")]
    public bool IsGracePeriodIncludedInTerm { get; set; }

    [Range(0, 10000000, ErrorMessage = "雜支費用不可超過1000萬")]
    [DisplayName("雜支費用")]
    public decimal MiscellaneousFees { get; set; }

    [Range(0, 50000000, ErrorMessage = "裝潢費用不可超過5000萬")]
    [DisplayName("裝潢費用")]
    public decimal RenovationCost { get; set; }

    [Required(ErrorMessage = "請選擇還款方式")]
    [DisplayName("還款方式")]
    public LoanType LoanType { get; set; }
}

public class LoanCalculationResult
{
    [DisplayName("每月還款金額")]
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal MonthlyPayment { get; set; }

    [DisplayName("總利息")]
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal TotalInterest { get; set; }

    [DisplayName("總還款金額")]
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal TotalPayment { get; set; }

    [DisplayName("初始資金金額")]
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal InitialCapital { get; set; }

    [DisplayName("總投資成本")]
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal TotalInvestmentCost { get; set; }

    public GracePeriodDetail? GracePeriodInfo { get; set; }
    public StagedCalculationSummary? StagedCalculationSummary { get; set; }
    public List<MonthlyPaymentDetail> PaymentSchedule { get; set; } = new();
    public List<ThreeYearPaymentDetail> ThreeYearSchedule { get; set; } = new();
    public List<ValidationWarning> Warnings { get; set; } = new();
    public List<ValidationInfo> Suggestions { get; set; } = new();
    
    public bool IsCalculationStable { get; set; } = true;
    public TimeSpan CalculationTime { get; set; }
}
```

### 輔助資料模型

```csharp
public class MonthlyPaymentDetail
{
    public int Period { get; set; }
    public DateTime PaymentDate { get; set; }
    
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal Payment { get; set; }
    
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal Principal { get; set; }
    
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal Interest { get; set; }
    
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal RemainingBalance { get; set; }
    
    public bool IsGracePeriod { get; set; }
    
    // 階段式利率相關屬性
    public int? StageNumber { get; set; }
    
    [DisplayFormat(DataFormatString = "{0:P3}")]
    public decimal? StageRate { get; set; }
}

public class ThreeYearPaymentDetail
{
    public int StartYear { get; set; }
    public int EndYear { get; set; }
    
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal MonthlyPayment { get; set; }
    
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal MonthlyPrincipal { get; set; }
    
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal MonthlyInterest { get; set; }
    
    [DisplayFormat(DataFormatString = "{0:P2}")]
    public decimal PrincipalRatio { get; set; }
    
    [DisplayFormat(DataFormatString = "{0:P2}")]
    public decimal InterestRatio { get; set; }
}

public class GracePeriodDetail
{
    public int GracePeriodMonths { get; set; }
    
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal GracePeriodMonthlyPayment { get; set; }
    
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal GracePeriodTotalInterest { get; set; }
    
    public decimal PostGracePeriodYears { get; set; }
    
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal PostGracePeriodMonthlyPayment { get; set; }
    
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal PaymentIncrease { get; set; }
    
    [DisplayFormat(DataFormatString = "{0:P2}")]
    public decimal PaymentIncreaseRatio { get; set; }
    
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal TotalInterestIncrease { get; set; }
}

/// <summary>
/// 階段摘要資料模型
/// </summary>
public class StageSummary
{
    [DisplayName("階段編號")]
    public int StageNumber { get; set; }
    
    [DisplayName("階段利率")]
    [DisplayFormat(DataFormatString = "{0:P3}")]
    public decimal StageRate { get; set; }
    
    [DisplayName("階段年限")]
    public int StageYears { get; set; }
    
    [DisplayName("階段月付金")]
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal StageMonthlyPayment { get; set; }
    
    [DisplayName("階段總利息")]
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal StageTotalInterest { get; set; }
    
    [DisplayName("階段總付款")]
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal StageTotalPayment { get; set; }
    
    [DisplayName("開始期數")]
    public int StartPeriod { get; set; }
    
    [DisplayName("結束期數")]
    public int EndPeriod { get; set; }
}

/// <summary>
/// 階段式計算摘要
/// </summary>
public class StagedCalculationSummary
{
    [DisplayName("階段摘要列表")]
    public List<StageSummary> StageSummaries { get; set; } = new();
    
    [DisplayName("加權平均利率")]
    [DisplayFormat(DataFormatString = "{0:P3}")]
    public decimal WeightedAverageRate { get; set; }
    
    [DisplayName("總階段數")]
    public int TotalStages { get; set; }
    
    [DisplayName("最高月付金")]
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal MaxMonthlyPayment => StageSummaries.Any() 
        ? StageSummaries.Max(s => s.StageMonthlyPayment) 
        : 0;
    
    [DisplayName("最低月付金")]
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal MinMonthlyPayment => StageSummaries.Any() 
        ? StageSummaries.Min(s => s.StageMonthlyPayment) 
        : 0;
    
    [DisplayName("月付金變化幅度")]
    [DisplayFormat(DataFormatString = "{0:C0}")]
    public decimal PaymentVariation => MaxMonthlyPayment - MinMonthlyPayment;
}
```

## 前端組件設計

### 智能切換輸入組件

```razor
@* LoanConditionSwitcher.razor *@
<div class="loan-condition-switcher">
    <div class="switcher-header">
        <label class="switcher-title">貸款條件設定</label>
        <div class="mode-toggle">
            <button type="button" 
                    class="btn @(InputMode == LoanInputMode.LoanToValueRatio ? "btn-primary" : "btn-outline-primary")"
                    @onclick="() => SwitchMode(LoanInputMode.LoanToValueRatio)">
                <i class="bi-bar-chart me-1"></i>成數模式
            </button>
            <button type="button" 
                    class="btn @(InputMode == LoanInputMode.LoanAmount ? "btn-primary" : "btn-outline-primary")"
                    @onclick="() => SwitchMode(LoanInputMode.LoanAmount)">
                <i class="bi-credit-card me-1"></i>金額模式
            </button>
        </div>
    </div>

    <div class="switcher-content">
        @if (InputMode == LoanInputMode.LoanToValueRatio)
        {
            <!-- 貸款成數模式：成數可編輯，金額顯示 -->
            <div class="row">
                <div class="col-md-6">
                    <IconInput IconClass="@LoanCalculatorIcons.LoanToValueRatio"
                              InputType="number"
                              @bind-Value="LoanToValueRatioPercent"
                              Placeholder="貸款成數 %"
                              ErrorMessage="@GetValidationError(nameof(LoanToValueRatio))" />
                    <small class="text-muted">可調整範圍：10% - 90%</small>
                </div>
                <div class="col-md-6">
                    <div class="calculated-display">
                        <div class="calculated-label">
                            <i class="@LoanCalculatorIcons.LoanAmount calculated-icon"></i>
                            <span>計算得出的貸款金額</span>
                        </div>
                        <div class="calculated-value">@CalculatedLoanAmount.ToString("C0")</div>
                    </div>
                </div>
            </div>
        }
        else
        {
            <!-- 貸款金額模式：金額可編輯，成數顯示 -->
            <div class="row">
                <div class="col-md-6">
                    <IconInput IconClass="@LoanCalculatorIcons.LoanAmount"
                              InputType="number"
                              @bind-Value="LoanAmount"
                              Placeholder="貸款金額"
                              ErrorMessage="@GetValidationError(nameof(LoanAmount))" />
                    <small class="text-muted">可貸範圍：@MinLoanAmount.ToString("C0") - @MaxLoanAmount.ToString("C0")</small>
                </div>
                <div class="col-md-6">
                    <div class="calculated-display">
                        <div class="calculated-label">
                            <i class="@LoanCalculatorIcons.LoanToValueRatio calculated-icon"></i>
                            <span>計算得出的貸款成數</span>
                        </div>
                        <div class="calculated-value">@CalculatedLoanToValueRatio.ToString("P1")</div>
                    </div>
                </div>
            </div>
        }
    </div>
</div>

@code {
    [Parameter] public LoanInputMode InputMode { get; set; } = LoanInputMode.LoanToValueRatio;
    [Parameter] public EventCallback<LoanInputMode> InputModeChanged { get; set; }
    
    [Parameter] public decimal LoanAmount { get; set; }
    [Parameter] public EventCallback<decimal> LoanAmountChanged { get; set; }
    
    [Parameter] public decimal LoanToValueRatio { get; set; }
    [Parameter] public EventCallback<decimal> LoanToValueRatioChanged { get; set; }
    
    [Parameter] public decimal HousePrice { get; set; }
    [Parameter] public decimal DownPayment { get; set; }
    
    [Parameter] public Func<string, string> GetValidationError { get; set; } = _ => "";

    // 百分比格式轉換
    private decimal LoanToValueRatioPercent
    {
        get => LoanToValueRatio * 100;
        set => LoanToValueRatioChanged.InvokeAsync(value / 100);
    }

    // 計算屬性
    private decimal CalculatedLoanAmount => (HousePrice - DownPayment) * LoanToValueRatio;
    private decimal CalculatedLoanToValueRatio => (HousePrice - DownPayment) > 0 
        ? LoanAmount / (HousePrice - DownPayment) 
        : 0;
    
    private decimal MinLoanAmount => Math.Max(100000, (HousePrice - DownPayment) * 0.1m);
    private decimal MaxLoanAmount => Math.Min(80000000, (HousePrice - DownPayment) * 0.9m);

    private async Task SwitchMode(LoanInputMode newMode)
    {
        if (InputMode != newMode)
        {
            // 切換前保存當前計算結果
            if (newMode == LoanInputMode.LoanAmount)
            {
                // 切換到金額模式：將計算出的金額設為輸入值
                await LoanAmountChanged.InvokeAsync(CalculatedLoanAmount);
            }
            else
            {
                // 切換到成數模式：將計算出的成數設為輸入值
                await LoanToValueRatioChanged.InvokeAsync(CalculatedLoanToValueRatio);
            }
            
            await InputModeChanged.InvokeAsync(newMode);
        }
    }
}
```

### 利率模式切換組件

```razor
@* InterestRateSwitcher.razor *@
<div class="interest-rate-switcher">
    <div class="switcher-header">
        <label class="switcher-title">利率設定</label>
        <div class="mode-toggle">
            <button type="button" 
                    class="btn @(RateMode == InterestRateMode.Fixed ? "btn-primary" : "btn-outline-primary")"
                    @onclick="() => SwitchMode(InterestRateMode.Fixed)">
                <i class="bi-lock me-1"></i>固定利率
            </button>
            <button type="button" 
                    class="btn @(RateMode == InterestRateMode.Staged ? "btn-primary" : "btn-outline-primary")"
                    @onclick="() => SwitchMode(InterestRateMode.Staged)">
                <i class="bi-graph-up me-1"></i>階段式利率
            </button>
        </div>
    </div>

    <div class="switcher-content">
        @if (RateMode == InterestRateMode.Fixed)
        {
            <!-- 固定利率模式：單一年利率輸入 -->
            <div class="row">
                <div class="col-md-6">
                    <IconInput IconClass="@LoanCalculatorIcons.InterestRate"
                              InputType="number"
                              @bind-Value="FixedAnnualRatePercent"
                              Placeholder="固定年利率 %"
                              ErrorMessage="@GetValidationError(nameof(FixedAnnualRate))" />
                    <small class="text-muted">可調整範圍：0.1% - 20%</small>
                </div>
                <div class="col-md-6">
                    <div class="calculated-field">
                        <label class="form-label text-muted">
                            <i class="@LoanCalculatorIcons.MonthlyRate me-1"></i>月利率 (計算值)
                        </label>
                        <div class="calculated-value">@CalculatedMonthlyRate.ToString("P3")</div>
                    </div>
                </div>
            </div>
        }
        else
        {
            <!-- 階段式利率模式：動態階段管理 -->
            <div class="staged-rate-config">
                <div class="stage-header mb-3">
                    <div class="d-flex justify-content-between align-items-center">
                        <h6 class="mb-0">利率階段設定</h6>
                        <button type="button" 
                                class="btn btn-outline-success btn-sm"
                                @onclick="AddStage"
                                disabled="@(!CanAddStage)">
                            <i class="bi-plus-circle me-1"></i>新增階段
                        </button>
                    </div>
                    <small class="text-muted">
                        總階段年限：@TotalStageYears 年 / 貸款年限：@LoanTermYears 年
                        @if (!IsYearsMatched)
                        {
                            <span class="text-danger">（年限不匹配）</span>
                        }
                    </small>
                </div>

                <div class="stages-list">
                    @for (int i = 0; i < StagedRateManager.Stages.Count; i++)
                    {
                        var stage = StagedRateManager.Stages[i];
                        var stageIndex = i;
                        
                        <div class="stage-item card mb-2" data-stage="@stage.StageNumber">
                            <div class="card-body p-3">
                                <div class="d-flex justify-content-between align-items-start mb-2">
                                    <h6 class="card-title mb-0">
                                        <i class="bi-calendar-event me-1"></i>階段 @stage.StageNumber
                                    </h6>
                                    @if (CanRemoveStage)
                                    {
                                        <button type="button" 
                                                class="btn btn-outline-danger btn-sm"
                                                @onclick="() => RemoveStage(stage.StageNumber)">
                                            <i class="bi-trash3"></i>
                                        </button>
                                    }
                                </div>
                                
                                <div class="row">
                                    <div class="col-md-6">
                                        <IconInput IconClass="@LoanCalculatorIcons.InterestRate"
                                                  InputType="number"
                                                  Value="@(stage.AnnualRate * 100)"
                                                  ValueChanged="@(value => UpdateStageRate(stageIndex, value / 100))"
                                                  Placeholder="階段年利率 %"
                                                  ErrorMessage="@GetStageValidationError(stageIndex, "Rate")" />
                                        <small class="text-muted">利率範圍：0.1% - 20%</small>
                                    </div>
                                    <div class="col-md-6">
                                        <IconInput IconClass="@LoanCalculatorIcons.TimeSpan"
                                                  InputType="number"
                                                  Value="@stage.Years"
                                                  ValueChanged="@(value => UpdateStageYears(stageIndex, value))"
                                                  Placeholder="階段年限"
                                                  ErrorMessage="@GetStageValidationError(stageIndex, "Years")" />
                                        <small class="text-muted">年限範圍：1 - @LoanTermYears 年</small>
                                    </div>
                                </div>
                            </div>
                        </div>
                    }
                </div>

                <div class="stage-summary mt-3">
                    <div class="row">
                        <div class="col-md-6">
                            <div class="calculated-field">
                                <label class="form-label text-muted">
                                    <i class="@LoanCalculatorIcons.AverageRate me-1"></i>加權平均年利率
                                </label>
                                <div class="calculated-value">@WeightedAverageRate.ToString("P3")</div>
                            </div>
                        </div>
                        <div class="col-md-6">
                            <div class="calculated-field">
                                <label class="form-label text-muted">
                                    <i class="@LoanCalculatorIcons.MonthlyRate me-1"></i>加權平均月利率
                                </label>
                                <div class="calculated-value">@WeightedAverageMonthlyRate.ToString("P3")</div>
                            </div>
                        </div>
                    </div>
                </div>

                @if (!IsYearsMatched)
                {
                    <div class="alert alert-warning d-flex align-items-center mt-3">
                        <i class="bi-exclamation-triangle me-2"></i>
                        <div>
                            <strong>年限不匹配：</strong>所有階段年限總和必須等於貸款年限。
                            <button type="button" 
                                    class="btn btn-link btn-sm p-0 ms-2"
                                    @onclick="AutoAdjustStages">
                                自動調整
                            </button>
                        </div>
                    </div>
                }

                @if (ValidationErrors.Any())
                {
                    <div class="alert alert-danger mt-3">
                        <strong>驗證錯誤：</strong>
                        <ul class="mb-0 mt-2">
                            @foreach (var error in ValidationErrors)
                            {
                                <li>@error</li>
                            }
                        </ul>
                    </div>
                }
            </div>
        }
    </div>
</div>

@code {
    [Parameter] public InterestRateMode RateMode { get; set; } = InterestRateMode.Fixed;
    [Parameter] public EventCallback<InterestRateMode> RateModeChanged { get; set; }
    
    [Parameter] public decimal FixedAnnualRate { get; set; } = 0.025m;
    [Parameter] public EventCallback<decimal> FixedAnnualRateChanged { get; set; }
    
    [Parameter] public StagedInterestRateManager StagedRateManager { get; set; } = new(30);
    [Parameter] public EventCallback<StagedInterestRateManager> StagedRateManagerChanged { get; set; }
    
    [Parameter] public int LoanTermYears { get; set; } = 30;
    [Parameter] public Func<string, string> GetValidationError { get; set; } = _ => "";

    // 百分比格式轉換
    private decimal FixedAnnualRatePercent
    {
        get => FixedAnnualRate * 100;
        set => FixedAnnualRateChanged.InvokeAsync(value / 100);
    }

    // 階段式利率計算屬性
    private int TotalStageYears => StagedRateManager.TotalYears;
    private bool IsYearsMatched => StagedRateManager.IsYearsMatched;
    private decimal WeightedAverageRate => StagedRateManager.WeightedAverageAnnualRate;
    private decimal WeightedAverageMonthlyRate => StagedRateManager.WeightedAverageMonthlyRate;
    private List<string> ValidationErrors => StagedRateManager.ValidateStages();
    
    private bool CanAddStage => StagedRateManager.Stages.Count < 10;
    private bool CanRemoveStage => StagedRateManager.Stages.Count > 1;

    // 固定利率計算屬性
    private decimal CalculatedMonthlyRate => FixedAnnualRate / 12;

    private async Task SwitchMode(InterestRateMode newMode)
    {
        if (RateMode != newMode)
        {
            await RateModeChanged.InvokeAsync(newMode);
        }
    }

    private async Task AddStage()
    {
        if (StagedRateManager.AddStage())
        {
            await StagedRateManagerChanged.InvokeAsync(StagedRateManager);
            StateHasChanged();
        }
    }

    private async Task RemoveStage(int stageNumber)
    {
        if (StagedRateManager.RemoveStage(stageNumber))
        {
            await StagedRateManagerChanged.InvokeAsync(StagedRateManager);
            StateHasChanged();
        }
    }

    private async Task UpdateStageRate(int stageIndex, decimal newRate)
    {
        if (stageIndex >= 0 && stageIndex < StagedRateManager.Stages.Count)
        {
            StagedRateManager.Stages[stageIndex].AnnualRate = newRate;
            await StagedRateManagerChanged.InvokeAsync(StagedRateManager);
            StateHasChanged();
        }
    }

    private async Task UpdateStageYears(int stageIndex, int newYears)
    {
        if (stageIndex >= 0 && stageIndex < StagedRateManager.Stages.Count)
        {
            StagedRateManager.Stages[stageIndex].Years = newYears;
            await StagedRateManagerChanged.InvokeAsync(StagedRateManager);
            StateHasChanged();
        }
    }

    private async Task AutoAdjustStages()
    {
        StagedRateManager.AutoAdjustToMatchLoanTerm();
        await StagedRateManagerChanged.InvokeAsync(StagedRateManager);
        StateHasChanged();
    }

    private string GetStageValidationError(int stageIndex, string property)
    {
        if (stageIndex < 0 || stageIndex >= StagedRateManager.Stages.Count)
            return "";

        var stage = StagedRateManager.Stages[stageIndex];
        
        return property switch
        {
            "Rate" when stage.AnnualRate <= 0 || stage.AnnualRate > 0.2m => "利率須介於0.1%到20%之間",
            "Years" when stage.Years <= 0 => "年限必須大於0",
            "Years" when stage.Years > LoanTermYears => $"年限不可超過貸款年限({LoanTermYears}年)",
            _ => ""
        };
    }
}
```

### 圖示輸入組件

```razor
@* IconInput.razor *@
<div class="form-input-with-icon @CssClass">
    <input type="@InputType"
           @bind="@Value"
           @bind:format="@Format"
           class="form-control @InputCssClass"
           placeholder="@Placeholder"
           disabled="@IsDisabled"
           @attributes="AdditionalAttributes" />
    <i class="@IconClass input-icon"></i>
    @if (!string.IsNullOrEmpty(ErrorMessage))
    {
        <div class="invalid-feedback">@ErrorMessage</div>
    }
</div>

@code {
    [Parameter] public string IconClass { get; set; } = "";
    [Parameter] public string InputType { get; set; } = "text";
    [Parameter] public object? Value { get; set; }
    [Parameter] public EventCallback<object> ValueChanged { get; set; }
    [Parameter] public string Format { get; set; } = "";
    [Parameter] public string Placeholder { get; set; } = "";
    [Parameter] public bool IsDisabled { get; set; } = false;
    [Parameter] public string ErrorMessage { get; set; } = "";
    [Parameter] public string CssClass { get; set; } = "";
    [Parameter] public string InputCssClass { get; set; } = "";
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }
}
```

### 圖示常數定義

```csharp
public static class LoanCalculatorIcons
{
    // 輸入欄位圖示
    public const string HousePrice = "bi-house-door";
    public const string DownPayment = "bi-wallet2";
    public const string LoanAmount = "bi-credit-card";
    public const string LoanToValueRatio = "bi-bar-chart";
    public const string LoanTermYears = "bi-clock";
    public const string InterestRate = "bi-graph-up";
    public const string MonthlyRate = "bi-calculator";
    public const string AdjustedRate = "bi-graph-up-arrow";
    public const string AverageRate = "bi-bar-chart-line";
    public const string TimeSpan = "bi-clock-history";
    public const string AnnualInterestRate = "bi-graph-up";
    public const string MonthlyInterestRate = "bi-calculator";
    public const string GracePeriod = "bi-hourglass-split";
    public const string GracePeriodOption = "bi-gear";
    public const string MiscellaneousFees = "bi-clipboard-check";
    public const string RenovationCost = "bi-palette";

    // 結果顯示圖示
    public const string TotalLoan = "bi-credit-card-2-back";
    public const string InitialCapital = "bi-cash-stack";
    public const string MonthlyPayment = "bi-calendar-check";
    public const string TotalInterest = "bi-graph-up-arrow";
    public const string TotalPayment = "bi-currency-dollar";
    public const string TotalInvestment = "bi-tags";
    public const string PaymentSchedule = "bi-table";
    public const string GracePeriodDetails = "bi-hourglass-bottom";

    // 狀態圖示
    public const string Success = "bi-check-circle";
    public const string Warning = "bi-exclamation-triangle";
    public const string Error = "bi-x-circle";
    public const string Info = "bi-info-circle";
    public const string Loading = "bi-arrow-clockwise";
}
```

### CSS 樣式設計

```css
/* 智能切換輸入組件樣式 */
.loan-condition-switcher,
.interest-rate-switcher {
    background: #ffffff;
    border: 1px solid #e9ecef;
    border-radius: 12px;
    padding: 24px;
    margin-bottom: 24px;
    box-shadow: 0 2px 8px rgba(0,0,0,0.1);
}

.switcher-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 20px;
    padding-bottom: 16px;
    border-bottom: 1px solid #e9ecef;
}

.switcher-title {
    font-size: 18px;
    font-weight: 600;
    color: #212529;
    margin: 0;
}

.mode-toggle {
    display: flex;
    gap: 8px;
}

.mode-toggle .btn {
    padding: 8px 16px;
    font-size: 14px;
    font-weight: 500;
    border-radius: 6px;
    transition: all 0.2s ease;
    display: flex;
    align-items: center;
}

.mode-toggle .btn:hover {
    transform: translateY(-1px);
    box-shadow: 0 2px 4px rgba(0,0,0,0.1);
}

.switcher-content {
    margin-top: 16px;
}

/* 利率模式特殊樣式 */
.floating-rate-config {
    background: #f8f9fa;
    border-radius: 8px;
    padding: 20px;
    margin-top: 16px;
}

.floating-rate-config .row {
    align-items: end;
}

.calculated-field {
    background: #ffffff;
    border: 2px dashed #dee2e6;
    border-radius: 8px;
    padding: 16px;
    text-align: center;
    height: auto;
    min-height: 80px;
    display: flex;
    flex-direction: column;
    justify-content: center;
}

.calculated-field .form-label {
    font-size: 14px;
    margin-bottom: 8px;
    color: #6c757d;
}

.calculated-field .calculated-value {
    font-size: 20px;
    font-weight: 700;
    color: #0d6efd;
    font-variant-numeric: tabular-nums;
    letter-spacing: 0.5px;
    margin: 0;
}

/* 計算結果顯示樣式 */
.calculated-display {
    background: #f8f9fa;
    border: 2px dashed #dee2e6;
    border-radius: 8px;
    padding: 16px;
    text-align: center;
    height: 100%;
    display: flex;
    flex-direction: column;
    justify-content: center;
}

.calculated-label {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 8px;
    font-size: 14px;
    color: #6c757d;
    margin-bottom: 8px;
}

.calculated-icon {
    font-size: 16px;
    color: #0d6efd;
}

.calculated-value {
    font-size: 24px;
    font-weight: 700;
    color: #0d6efd;
    font-variant-numeric: tabular-nums;
    letter-spacing: 0.5px;
}

/* 響應式設計 */
@media (max-width: 768px) {
    .switcher-header {
        flex-direction: column;
        gap: 16px;
        align-items: stretch;
    }
    
    .mode-toggle {
        width: 100%;
    }
    
    .mode-toggle .btn {
        flex: 1;
        justify-content: center;
    }
    
    .calculated-display {
        margin-top: 16px;
    }
}

/* 圖示輸入欄位樣式 */
.form-input-with-icon {
    position: relative;
}

.form-input-with-icon .input-icon {
    position: absolute;
    left: 12px;
    top: 50%;
    transform: translateY(-50%);
    color: #6c757d;
    font-size: 16px;
    pointer-events: none;
    z-index: 2;
}

.form-input-with-icon input {
    padding-left: 40px;
}

.form-input-with-icon input:focus + .input-icon {
    color: #0d6efd;
}

.form-input-with-icon.has-error .input-icon {
    color: #dc3545;
}

.form-input-with-icon.has-success .input-icon {
    color: #198754;
}

/* 結果卡片樣式 */
.result-card {
    background: #ffffff;
    border: 1px solid #e9ecef;
    border-radius: 8px;
    padding: 20px;
    margin-bottom: 16px;
    box-shadow: 0 2px 4px rgba(0,0,0,0.1);
}

.result-item {
    display: flex;
    align-items: center;
    margin-bottom: 12px;
}

.result-icon {
    font-size: 20px;
    margin-right: 12px;
    width: 24px;
    text-align: center;
}

.result-label {
    font-weight: 500;
    color: #495057;
    margin-right: auto;
}

.result-value {
    font-size: 18px;
    font-weight: 600;
    color: #212529;
}

.result-value.primary { color: #0d6efd; }
.result-value.success { color: #198754; }
.result-value.warning { color: #fd7e14; }
.result-value.danger { color: #dc3545; }
.result-value.info { color: #20c997; }

/* 數字格式化樣式 */
.currency-input {
    text-align: right;
    font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
    font-variant-numeric: tabular-nums;
}

.currency-display {
    font-weight: 600;
    color: #2d5016;
    font-variant-numeric: tabular-nums;
    letter-spacing: 0.5px;
}
```

## 服務層設計

### 房貸計算服務介面

```csharp
public interface ILoanCalculationService
{
    /// <summary>
    /// 執行房貸計算
    /// </summary>
    Task<LoanCalculationResult> CalculateAsync(LoanCalculationInput input);

    /// <summary>
    /// 產生還款明細表
    /// </summary>
    Task<List<MonthlyPaymentDetail>> GeneratePaymentScheduleAsync(LoanCalculationInput input);

    /// <summary>
    /// 產生三年期間明細
    /// </summary>
    Task<List<ThreeYearPaymentDetail>> GenerateThreeYearScheduleAsync(LoanCalculationInput input);
}
```

### 驗證服務介面

```csharp
public interface IValidationService
{
    /// <summary>
    /// 驗證輸入資料
    /// </summary>
    Task<(bool IsValid, List<ValidationWarning> Warnings, List<ValidationInfo> InfoMessages)> 
        ValidateAsync(LoanCalculationInput input);

    /// <summary>
    /// 檢查計算結果異常
    /// </summary>
    bool ValidateCalculationResult(LoanCalculationResult result);

    /// <summary>
    /// 產生智慧修正建議
    /// </summary>
    List<ValidationInfo> GenerateSmartSuggestions(LoanCalculationInput input);
}
```

## 數字格式化處理

### .NET 格式化設定

```csharp
// Program.cs 中的文化設定
services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { "zh-TW" };
    options.SetDefaultCulture(supportedCultures[0])
           .AddSupportedCultures(supportedCultures)
           .AddSupportedUICultures(supportedCultures);
});

CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("zh-TW");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("zh-TW");
```

### 格式化輔助方法

```csharp
public static class FormatHelper
{
    public static string FormatCurrency(decimal amount)
    {
        return amount.ToString("C0", new CultureInfo("zh-TW"));
    }

    public static string FormatNumber(decimal amount)
    {
        return amount.ToString("N0", new CultureInfo("zh-TW"));
    }

    public static string FormatPercentage(decimal rate, int decimals = 3)
    {
        return rate.ToString($"P{decimals}", new CultureInfo("zh-TW"));
    }

    public static decimal ParseFormattedNumber(string formattedString)
    {
        if (string.IsNullOrEmpty(formattedString))
            return 0;

        var cleanString = formattedString.Replace(",", "").Replace("NT$", "");
        return decimal.TryParse(cleanString, out var result) ? result : 0;
    }
}
```

### JavaScript 輔助函式

```javascript
// 自動格式化數字輸入
function formatNumberInput(input) {
    let value = input.value.replace(/[^0-9]/g, '');
    
    if (value.length > 0) {
        value = parseInt(value).toLocaleString('zh-TW');
    }
    
    input.value = value;
}

// 解析格式化的數字字串
function parseFormattedNumber(formattedString) {
    return parseInt(formattedString.replace(/,/g, '')) || 0;
}

// 即時格式化綁定
function bindNumberFormatting() {
    document.querySelectorAll('.currency-input').forEach(input => {
        input.addEventListener('input', () => formatNumberInput(input));
        input.addEventListener('blur', () => formatNumberInput(input));
    });
}
```

## 驗證機制實作

### 自訂驗證屬性

```csharp
public class LoanAmountValidationAttribute : ValidationAttribute
{
    protected override ValidationResult IsValid(object value, ValidationContext validationContext)
    {
        var input = (LoanCalculationInput)validationContext.ObjectInstance;
        var loanAmount = (decimal)value;

        if (loanAmount > input.HousePrice)
        {
            return new ValidationResult("貸款金額不可超過房屋總價");
        }

        if (input.DownPayment + loanAmount < input.HousePrice)
        {
            return new ValidationResult("自備款加貸款金額不可小於房屋總價");
        }

        return ValidationResult.Success;
    }
}
```

### 業務邏輯驗證

```csharp
public class LoanValidationService : IValidationService
{
    public async Task<(bool IsValid, List<ValidationWarning> Warnings, List<ValidationInfo> InfoMessages)> 
        ValidateAsync(LoanCalculationInput input)
    {
        var warnings = new List<ValidationWarning>();
        var infoMessages = new List<ValidationInfo>();
        var isValid = true;

        // 範圍驗證
        if (input.LoanToValueRatio > 0.85m)
        {
            warnings.Add(new ValidationWarning
            {
                Type = ValidationMessageType.Warning,
                Message = "貸款成數偏高 (>85%)，建議評估風險",
                Suggestion = "考慮增加自備款以降低貸款成數"
            });
        }

        // 財務合理性檢查
        var monthlyPayment = await CalculateMonthlyPayment(input);
        if (monthlyPayment > input.HousePrice * 0.005m) // 假設月收入不應超過房價的0.5%
        {
            warnings.Add(new ValidationWarning
            {
                Type = ValidationMessageType.Warning,
                Message = "每月還款負擔可能過重",
                Suggestion = "建議延長貸款年限或增加自備款"
            });
        }

        // 資訊性建議
        if (input.DownPayment < input.HousePrice * 0.2m)
        {
            infoMessages.Add(new ValidationInfo
            {
                Title = "自備款建議",
                Message = "建議自備款至少為房價 20%",
                ActionText = "調整為 20%",
                ActionValue = (input.HousePrice * 0.2m).ToString()
            });
        }

        return (isValid, warnings, infoMessages);
    }
}
```

## 專案時程規劃

### Phase 1: 基礎功能 (2.5週)

#### Week 1: 核心計算邏輯

- [ ] **計算公式實作** (3天)
  - 本息攤還計算算法
  - 本金攤還計算算法
  - 寬限期計算邏輯
  - 單元測試覆蓋

- [ ] **資料模型建立** (2天)
  - 輸入模型定義
  - 結果模型定義
  - 驗證屬性設定
  - 格式化方法實作

  - 單元測試覆蓋

#### Week 1.5: 驗證系統

- [ ] **驗證服務實作** (2天)
  - 輸入驗證規則
  - 邏輯一致性檢查
  - 智慧建議機制

- [ ] **錯誤處理機制** (1天)
  - 異常偵測
  - 錯誤訊息分級
  - 使用者友善提示

#### Week 2-2.5: UI 介面開發

- [ ] **基礎組件開發** (3天)
  - IconInput 組件
  - LoanInputForm 組件
  - ResultDisplay 組件
  - 圖示整合

- [ ] **互動功能實作** (2天)
  - 即時計算更新
  - 欄位聯動邏輯
  - 驗證狀態顯示

### Phase 2: 進階功能 (2週)

- [ ] **多種還款方式** (3天)
- [ ] **圖表視覺化** (4天)
- [ ] **利率情境分析** (3天)

### Phase 3: 完善功能 (1週)

- [ ] **方案比較功能** (2天)
- [ ] **匯出功能** (2天)
- [ ] **使用者體驗最佳化** (3天)

### Phase 4: 最佳化與部署 (1週)

- [ ] **效能最佳化** (2天)
- [ ] **安全性檢查** (1天)
- [ ] **部署與上線** (2天)
- [ ] **文件整理** (2天)

## 測試策略

### 單元測試

```csharp
[TestFixture]
public class LoanCalculationServiceTests
{
    private ILoanCalculationService _service;

    [SetUp]
    public void Setup()
    {
        _service = new LoanCalculationService();
    }

    [Test]
    public void CalculateEqualInstallment_Should_Return_Correct_Amount()
    {
        // Arrange
        var principal = 1000000m;
        var monthlyRate = 0.02m / 12;
        var periods = 30 * 12;

        // Act
        var result = _service.CalculateEqualInstallment(principal, monthlyRate, periods);

        // Assert
        Assert.That(result, Is.EqualTo(3696.24m).Within(0.01m));
    }

    [Test]
    public void ValidateInput_Should_Return_Error_When_LoanAmount_Exceeds_HousePrice()
    {
        // Test implementation
    }
}
```

### 整合測試

```csharp
[TestFixture]
public class LoanCalculatorIntegrationTests
{
    [Test]
    public async Task FullLoanCalculation_Should_Work_EndToEnd()
    {
        // Test complete calculation flow
    }
}
```

## 效能考量

### 計算最佳化

```csharp
public class OptimizedLoanCalculationService : ILoanCalculationService
{
    private readonly IMemoryCache _cache;

    // 使用快取避免重複計算
    public async Task<LoanCalculationResult> CalculateAsync(LoanCalculationInput input)
    {
        var cacheKey = GenerateCacheKey(input);
        
        if (_cache.TryGetValue(cacheKey, out LoanCalculationResult cachedResult))
        {
            return cachedResult;
        }

        var result = await PerformCalculation(input);
        
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(10));
        
        return result;
    }
}
```

### 前端效能

```javascript
// 防抖動輸入處理
let debounceTimer;
function debounceCalculation(inputFunction, delay = 300) {
    clearTimeout(debounceTimer);
    debounceTimer = setTimeout(inputFunction, delay);
}

// 虛擬化長列表
function renderPaymentSchedule(data, startIndex, endIndex) {
    // 只渲染可見範圍的項目
}
```

## 部署與維護

### Docker 化

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["HouseTrackerApp/HouseTrackerApp.csproj", "HouseTrackerApp/"]
RUN dotnet restore "HouseTrackerApp/HouseTrackerApp.csproj"
COPY . .
WORKDIR "/src/HouseTrackerApp"
RUN dotnet build "HouseTrackerApp.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "HouseTrackerApp.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "HouseTrackerApp.dll"]
```

### 監控與日誌

```csharp
// 結構化日誌
public class LoanCalculationService : ILoanCalculationService
{
    private readonly ILogger<LoanCalculationService> _logger;

    public async Task<LoanCalculationResult> CalculateAsync(LoanCalculationInput input)
    {
        using var scope = _logger.BeginScope("LoanCalculation");
        
        _logger.LogInformation("開始計算房貸，貸款金額: {LoanAmount}", input.LoanAmount);
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var result = await PerformCalculation(input);
            
            _logger.LogInformation("計算完成，耗時: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "計算過程發生錯誤");
            throw;
        }
    }
}
```

---

*此文件定義房貸試算器的技術實作規格，功能需求請參考 [REQUIREMENTS.md](./REQUIREMENTS.md)*
