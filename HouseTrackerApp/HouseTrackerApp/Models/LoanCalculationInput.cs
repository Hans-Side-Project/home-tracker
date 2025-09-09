using System.ComponentModel.DataAnnotations;

namespace HouseTrackerApp.Models;

public class LoanCalculationInput
{
    [Required]
    [Range(1_000_000, 100_000_000, ErrorMessage = "房屋總價須介於100萬至1億新台幣之間")]
    public decimal HousePrice { get; set; } = 10_000_000; // 預設1000萬

    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "自備款不可為負數")]
    public decimal DownPayment { get; set; } = 2_000_000; // 預設200萬

    // 貸款條件模式：true=貸款成數模式, false=貸款金額模式
    public bool IsLoanRatioMode { get; set; } = true;

    [Range(10, 90, ErrorMessage = "貸款成數須介於10%至90%之間")]
    public decimal LoanRatio { get; set; } = 80; // 預設80%

    [Range(100_000, 80_000_000, ErrorMessage = "貸款金額須介於10萬至8000萬新台幣之間")]
    public decimal LoanAmount { get; set; }

    [Required]
    [Range(1, 40, ErrorMessage = "貸款年限須介於1年至40年之間")]
    public int LoanYears { get; set; } = 30; // 預設30年

    // 利率模式：true=固定利率, false=階段式利率
    public bool IsFixedRateMode { get; set; } = true;

    [Range(0.1, 20.0, ErrorMessage = "年利率須介於0.1%至20%之間")]
    public decimal FixedAnnualRate { get; set; } = 2.5m; // 預設2.5%

    public List<InterestRateStage> InterestRateStages { get; set; } = new();

    // 寬限期模式：true=無寬限期, false=有寬限期
    public bool IsNoGracePeriodMode { get; set; } = true;

    [Range(1, 5, ErrorMessage = "寬限期須介於1年至5年之間")]
    public int GracePeriodYears { get; set; } = 1;

    // 寬限期是否包含在貸款年限內
    public bool IsGracePeriodIncluded { get; set; } = true;

    [Range(0, 10_000_000, ErrorMessage = "雜支費用不可超過1000萬新台幣")]
    public decimal MiscellaneousFees { get; set; } = 0;

    [Range(0, 50_000_000, ErrorMessage = "裝潢費用不可超過5000萬新台幣")]
    public decimal RenovationFees { get; set; } = 0;

    // 還款方式：EqualInstallment=本息攤還, EqualPrincipal=本金攤還
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.EqualInstallment;

    // 計算屬性
    public decimal MonthlyFixedRate => FixedAnnualRate / 100 / 12;

    public int TotalMonths
    {
        get
        {
            if (IsNoGracePeriodMode)
                return LoanYears * 12;

            return IsGracePeriodIncluded
                ? LoanYears * 12
                : (LoanYears + GracePeriodYears) * 12;
        }
    }

    public int GracePeriodMonths => IsNoGracePeriodMode ? 0 : GracePeriodYears * 12;

    public int RepaymentMonths
    {
        get
        {
            if (IsNoGracePeriodMode)
                return LoanYears * 12;

            return IsGracePeriodIncluded
                ? (LoanYears - GracePeriodYears) * 12
                : LoanYears * 12;
        }
    }

    public decimal TotalInvestmentCost => HousePrice + MiscellaneousFees + RenovationFees;

    public decimal InitialCash => DownPayment + MiscellaneousFees + RenovationFees;

    // 根據模式計算實際貸款金額
    public decimal CalculatedLoanAmount
    {
        get
        {
            if (IsLoanRatioMode)
                return (HousePrice - DownPayment) * LoanRatio / 100;
            else
                return LoanAmount;
        }
    }

    // 根據模式計算實際貸款成數
    public decimal CalculatedLoanRatio
    {
        get
        {
            if (!IsLoanRatioMode && HousePrice > DownPayment)
                return LoanAmount / (HousePrice - DownPayment) * 100;
            else
                return LoanRatio;
        }
    }
}

public class InterestRateStage
{
    [Range(0.1, 20.0, ErrorMessage = "年利率須介於0.1%至20%之間")]
    public decimal AnnualRate { get; set; } = 2.5m;

    [Range(1, 40, ErrorMessage = "階段年限須介於1年至40年之間")]
    public int Years { get; set; } = 1;

    public decimal MonthlyRate => AnnualRate / 100 / 12;

    public int Months => Years * 12;
}

public enum PaymentMethod
{
    EqualInstallment, // 本息攤還
    EqualPrincipal,   // 本金攤還
    InterestOnly      // 只繳利息（寬限期）
}
