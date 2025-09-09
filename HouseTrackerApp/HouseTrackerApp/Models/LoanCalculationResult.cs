namespace HouseTrackerApp.Models;

public class LoanCalculationResult
{
    public decimal LoanAmount { get; set; }
    public decimal InitialCash { get; set; }
    public decimal MonthlyPayment { get; set; }
    public decimal TotalInterest { get; set; }
    public decimal TotalPayment { get; set; }
    public decimal TotalInvestmentCost { get; set; }

    // 寬限期相關結果
    public GracePeriodResult? GracePeriodResult { get; set; }

    // 還款明細表（每三年一個級距）
    public List<PaymentPeriodSummary> PaymentSummaries { get; set; } = new();

    // 詳細還款計劃
    public List<MonthlyPaymentDetail> PaymentSchedule { get; set; } = new();

    // 利息與本金比例分析
    public InterestPrincipalAnalysis Analysis { get; set; } = new();
}

public class GracePeriodResult
{
    public int GracePeriodMonths { get; set; }
    public decimal GracePeriodMonthlyPayment { get; set; } // 寬限期每月還款（僅利息）
    public decimal GracePeriodTotalInterest { get; set; } // 寬限期總利息
    public decimal RemainingPrincipalAfterGrace { get; set; } // 寬限期後剩餘本金

    // 寬限期後還款資訊
    public int RepaymentMonthsAfterGrace { get; set; }
    public decimal MonthlyPaymentAfterGrace { get; set; }
    public decimal TotalInterestAfterGrace { get; set; }

    // 影響分析
    public decimal PaymentIncreaseAmount { get; set; } // 月付金增加金額
    public decimal PaymentIncreasePercentage { get; set; } // 月付金增加比例
    public decimal TotalInterestIncrease { get; set; } // 總利息增加金額
    public int TotalRepaymentPeriod { get; set; } // 總還款期間（月）
}

public class PaymentPeriodSummary
{
    public string PeriodDescription { get; set; } = string.Empty; // 如：第1-3年
    public int StartYear { get; set; }
    public int EndYear { get; set; }
    public decimal MonthlyPayment { get; set; }
    public decimal MonthlyPrincipal { get; set; }
    public decimal MonthlyInterest { get; set; }
    public decimal PrincipalPercentage { get; set; }
    public decimal InterestPercentage { get; set; }
    public decimal PeriodTotalPayment { get; set; }
    public decimal PeriodTotalPrincipal { get; set; }
    public decimal PeriodTotalInterest { get; set; }
    public decimal RemainingBalance { get; set; } // 期末餘額
}

public class MonthlyPaymentDetail
{
    public int Period { get; set; } // 第幾期
    public int Year { get; set; } // 第幾年
    public int Month { get; set; } // 第幾月
    public decimal Payment { get; set; } // 當期還款金額
    public decimal Principal { get; set; } // 當期本金
    public decimal Interest { get; set; } // 當期利息
    public decimal RemainingBalance { get; set; } // 剩餘本金
    public decimal CumulativePayment { get; set; } // 累計還款
    public decimal CumulativePrincipal { get; set; } // 累計本金
    public decimal CumulativeInterest { get; set; } // 累計利息
    public decimal CurrentRate { get; set; } // 當期適用利率（月利率）
    public bool IsGracePeriod { get; set; } // 是否為寬限期
}

public class InterestPrincipalAnalysis
{
    public decimal TotalPrincipalPayment { get; set; }
    public decimal TotalInterestPayment { get; set; }
    public decimal PrincipalPercentage { get; set; }
    public decimal InterestPercentage { get; set; }
    
    // 各年度本息比例分析
    public List<YearlyAnalysis> YearlyBreakdown { get; set; } = new();
}

public class YearlyAnalysis
{
    public int Year { get; set; }
    public decimal YearlyPayment { get; set; }
    public decimal YearlyPrincipal { get; set; }
    public decimal YearlyInterest { get; set; }
    public decimal YearlyPrincipalPercentage { get; set; }
    public decimal YearlyInterestPercentage { get; set; }
    public decimal YearEndBalance { get; set; }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<ValidationError> Errors { get; set; } = new();
    public List<ValidationWarning> Warnings { get; set; } = new();
    public List<ValidationInfo> Infos { get; set; } = new();
}

public class ValidationError
{
    public string PropertyName { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public ValidationSeverity Severity { get; set; } = ValidationSeverity.Error;
}

public class ValidationWarning
{
    public string PropertyName { get; set; } = string.Empty;
    public string WarningMessage { get; set; } = string.Empty;
    public string? SuggestionMessage { get; set; }
}

public class ValidationInfo
{
    public string PropertyName { get; set; } = string.Empty;
    public string InfoMessage { get; set; } = string.Empty;
    public string? RecommendationMessage { get; set; }
}

public enum ValidationSeverity
{
    Info,    // 資訊性提示
    Warning, // 警告但可執行
    Error    // 錯誤必須修正
}
