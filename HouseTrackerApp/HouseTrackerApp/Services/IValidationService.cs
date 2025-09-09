using HouseTrackerApp.Models;

namespace HouseTrackerApp.Services;

public interface IValidationService
{
    /// <summary>
    /// 驗證貸款輸入參數
    /// </summary>
    /// <param name="input">貸款輸入參數</param>
    /// <returns>驗證結果</returns>
    ValidationResult ValidateInput(LoanCalculationInput input);

    /// <summary>
    /// 驗證貸款條件邏輯一致性
    /// </summary>
    /// <param name="input">貸款輸入參數</param>
    /// <returns>驗證結果</returns>
    ValidationResult ValidateLoanConditions(LoanCalculationInput input);

    /// <summary>
    /// 驗證利率階段設定
    /// </summary>
    /// <param name="stages">利率階段列表</param>
    /// <param name="totalYears">總年限</param>
    /// <returns>驗證結果</returns>
    ValidationResult ValidateInterestRateStages(List<InterestRateStage> stages, int totalYears);

    /// <summary>
    /// 驗證寬限期設定
    /// </summary>
    /// <param name="input">貸款輸入參數</param>
    /// <returns>驗證結果</returns>
    ValidationResult ValidateGracePeriod(LoanCalculationInput input);

    /// <summary>
    /// 取得修正建議
    /// </summary>
    /// <param name="input">貸款輸入參數</param>
    /// <returns>修正建議列表</returns>
    List<string> GetCorrectionSuggestions(LoanCalculationInput input);

    /// <summary>
    /// 檢查財務合理性
    /// </summary>
    /// <param name="input">貸款輸入參數</param>
    /// <returns>驗證結果</returns>
    ValidationResult CheckFinancialReasonableness(LoanCalculationInput input);
}
