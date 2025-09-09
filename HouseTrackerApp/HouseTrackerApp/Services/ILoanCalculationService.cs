using HouseTrackerApp.Models;

namespace HouseTrackerApp.Services;

public interface ILoanCalculationService
{
    /// <summary>
    /// 計算房貸試算結果
    /// </summary>
    /// <param name="input">貸款輸入參數</param>
    /// <returns>計算結果</returns>
    LoanCalculationResult Calculate(LoanCalculationInput input);

    /// <summary>
    /// 計算本息攤還月付金
    /// </summary>
    /// <param name="principal">本金</param>
    /// <param name="monthlyRate">月利率</param>
    /// <param name="periods">期數</param>
    /// <returns>月付金</returns>
    decimal CalculateEqualInstallment(decimal principal, decimal monthlyRate, int periods);

    /// <summary>
    /// 計算本金攤還還款明細
    /// </summary>
    /// <param name="principal">本金</param>
    /// <param name="monthlyRate">月利率</param>
    /// <param name="periods">期數</param>
    /// <returns>還款明細</returns>
    List<MonthlyPaymentDetail> CalculateEqualPrincipal(decimal principal, decimal monthlyRate, int periods);

    /// <summary>
    /// 計算寬限期還款結果
    /// </summary>
    /// <param name="input">貸款輸入參數</param>
    /// <returns>寬限期計算結果</returns>
    GracePeriodResult? CalculateGracePeriodResult(LoanCalculationInput input);

    /// <summary>
    /// 計算階段式利率還款明細
    /// </summary>
    /// <param name="principal">本金</param>
    /// <param name="stages">利率階段</param>
    /// <param name="paymentMethod">還款方式</param>
    /// <returns>還款明細</returns>
    List<MonthlyPaymentDetail> CalculateStageRates(decimal principal, List<InterestRateStage> stages, PaymentMethod paymentMethod);

    /// <summary>
    /// 產生每三年期間還款摘要
    /// </summary>
    /// <param name="paymentSchedule">詳細還款計劃</param>
    /// <returns>期間摘要列表</returns>
    List<PaymentPeriodSummary> GeneratePaymentSummaries(List<MonthlyPaymentDetail> paymentSchedule);

    /// <summary>
    /// 計算利息與本金分析
    /// </summary>
    /// <param name="paymentSchedule">詳細還款計劃</param>
    /// <returns>分析結果</returns>
    InterestPrincipalAnalysis AnalyzeInterestPrincipal(List<MonthlyPaymentDetail> paymentSchedule);
}
