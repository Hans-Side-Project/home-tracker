using HouseTrackerApp.Models;

namespace HouseTrackerApp.Services;

public class ValidationService : IValidationService
{
    public ValidationResult ValidateInput(LoanCalculationInput input)
    {
        var result = new ValidationResult { IsValid = true };

        // 基本數值範圍驗證
        ValidateBasicRanges(input, result);

        // 貸款條件邏輯驗證
        var loanConditionsResult = ValidateLoanConditions(input);
        result.Errors.AddRange(loanConditionsResult.Errors);
        result.Warnings.AddRange(loanConditionsResult.Warnings);
        result.Infos.AddRange(loanConditionsResult.Infos);

        // 利率階段驗證
        if (!input.IsFixedRateMode)
        {
            var stageResult = ValidateInterestRateStages(input.InterestRateStages, input.LoanYears);
            result.Errors.AddRange(stageResult.Errors);
            result.Warnings.AddRange(stageResult.Warnings);
        }

        // 寬限期驗證
        var gracePeriodResult = ValidateGracePeriod(input);
        result.Errors.AddRange(gracePeriodResult.Errors);
        result.Warnings.AddRange(gracePeriodResult.Warnings);

        // 財務合理性檢查
        var financialResult = CheckFinancialReasonableness(input);
        result.Warnings.AddRange(financialResult.Warnings);
        result.Infos.AddRange(financialResult.Infos);

        result.IsValid = !result.Errors.Any();

        return result;
    }

    public ValidationResult ValidateLoanConditions(LoanCalculationInput input)
    {
        var result = new ValidationResult { IsValid = true };

        // 檢查自備款不可超過房屋總價
        if (input.DownPayment > input.HousePrice)
        {
            result.Errors.Add(new ValidationError
            {
                PropertyName = nameof(input.DownPayment),
                ErrorMessage = "自備款不可超過房屋總價",
                Severity = ValidationSeverity.Error
            });
        }

        // 檢查可貸金額
        var maxLoanAmount = input.HousePrice - input.DownPayment;
        if (maxLoanAmount <= 0)
        {
            result.Errors.Add(new ValidationError
            {
                PropertyName = nameof(input.LoanAmount),
                ErrorMessage = "可貸金額必須大於零，請調整房屋總價或自備款",
                Severity = ValidationSeverity.Error
            });
        }

        // 驗證貸款條件模式邏輯
        if (input.IsLoanRatioMode)
        {
            // 貸款成數模式：檢查計算出的貸款金額是否合理
            var calculatedLoanAmount = input.CalculatedLoanAmount;
            if (calculatedLoanAmount > maxLoanAmount)
            {
                result.Errors.Add(new ValidationError
                {
                    PropertyName = nameof(input.LoanRatio),
                    ErrorMessage = "以此貸款成數計算的貸款金額超過可貸金額上限",
                    Severity = ValidationSeverity.Error
                });
            }
        }
        else
        {
            // 貸款金額模式：檢查貸款金額是否超過可貸金額
            if (input.LoanAmount > maxLoanAmount)
            {
                result.Errors.Add(new ValidationError
                {
                    PropertyName = nameof(input.LoanAmount),
                    ErrorMessage = $"貸款金額不可超過可貸金額上限 {maxLoanAmount:N0} 元",
                    Severity = ValidationSeverity.Error
                });
            }
        }

        // 貸款成數警告
        var actualLoanRatio = input.CalculatedLoanRatio;
        if (actualLoanRatio > 80)
        {
            result.Warnings.Add(new ValidationWarning
            {
                PropertyName = nameof(input.LoanRatio),
                WarningMessage = $"貸款成數偏高 ({actualLoanRatio:F1}%)，可能影響貸款申請成功率",
                SuggestionMessage = "建議考慮增加自備款或選擇較低房價的物件"
            });
        }

        result.IsValid = !result.Errors.Any();
        return result;
    }

    public ValidationResult ValidateInterestRateStages(List<InterestRateStage> stages, int totalYears)
    {
        var result = new ValidationResult { IsValid = true };

        if (!stages.Any())
        {
            result.Errors.Add(new ValidationError
            {
                PropertyName = nameof(stages),
                ErrorMessage = "階段式利率至少須設定一個階段",
                Severity = ValidationSeverity.Error
            });
            return result;
        }

        if (stages.Count > 10)
        {
            result.Errors.Add(new ValidationError
            {
                PropertyName = nameof(stages),
                ErrorMessage = "階段式利率最多可設定10個階段",
                Severity = ValidationSeverity.Error
            });
        }

        // 檢查每個階段的設定
        for (int i = 0; i < stages.Count; i++)
        {
            var stage = stages[i];
            
            if (stage.AnnualRate < 0.1m || stage.AnnualRate > 20m)
            {
                result.Errors.Add(new ValidationError
                {
                    PropertyName = $"stages[{i}].AnnualRate",
                    ErrorMessage = $"第{i + 1}階段年利率須介於0.1%至20%之間",
                    Severity = ValidationSeverity.Error
                });
            }

            if (stage.Years < 1 || stage.Years > totalYears)
            {
                result.Errors.Add(new ValidationError
                {
                    PropertyName = $"stages[{i}].Years",
                    ErrorMessage = $"第{i + 1}階段年限須介於1年至{totalYears}年之間",
                    Severity = ValidationSeverity.Error
                });
            }
        }

        // 檢查總年限是否等於貸款年限
        var totalStageYears = stages.Sum(s => s.Years);
        if (totalStageYears != totalYears)
        {
            result.Errors.Add(new ValidationError
            {
                PropertyName = nameof(stages),
                ErrorMessage = $"所有階段年限總和 ({totalStageYears}年) 必須等於貸款年限 ({totalYears}年)",
                Severity = ValidationSeverity.Error
            });

            // 提供調整建議
            if (totalStageYears < totalYears)
            {
                result.Infos.Add(new ValidationInfo
                {
                    PropertyName = nameof(stages),
                    InfoMessage = $"建議增加 {totalYears - totalStageYears} 年到現有階段或新增階段",
                    RecommendationMessage = "可在最後一個階段增加年限或新增一個階段"
                });
            }
            else
            {
                result.Infos.Add(new ValidationInfo
                {
                    PropertyName = nameof(stages),
                    InfoMessage = $"建議減少 {totalStageYears - totalYears} 年從現有階段",
                    RecommendationMessage = "可從最後一個階段減少年限或刪除多餘階段"
                });
            }
        }

        result.IsValid = !result.Errors.Any();
        return result;
    }

    public ValidationResult ValidateGracePeriod(LoanCalculationInput input)
    {
        var result = new ValidationResult { IsValid = true };

        if (input.IsNoGracePeriodMode)
            return result; // 無寬限期模式不需要驗證

        // 寬限期年限不可超過貸款年限
        if (input.GracePeriodYears >= input.LoanYears)
        {
            result.Errors.Add(new ValidationError
            {
                PropertyName = nameof(input.GracePeriodYears),
                ErrorMessage = "寬限期年限不可等於或超過貸款年限",
                Severity = ValidationSeverity.Error
            });
        }

        // 當寬限期包含在貸款年限內時，檢查實際還款期間是否合理
        if (input.IsGracePeriodIncluded)
        {
            var actualRepaymentYears = input.LoanYears - input.GracePeriodYears;
            if (actualRepaymentYears < 1)
            {
                result.Errors.Add(new ValidationError
                {
                    PropertyName = nameof(input.GracePeriodYears),
                    ErrorMessage = "寬限期包含在貸款年限內時，實際還款期間必須至少1年",
                    Severity = ValidationSeverity.Error
                });
            }
            else if (actualRepaymentYears < 5)
            {
                result.Warnings.Add(new ValidationWarning
                {
                    PropertyName = nameof(input.GracePeriodYears),
                    WarningMessage = $"實際還款期間僅 {actualRepaymentYears} 年，月付金可能較高",
                    SuggestionMessage = "建議延長貸款年限或縮短寬限期"
                });
            }
        }

        // 寬限期風險提醒
        if (input.GracePeriodYears >= 3)
        {
            result.Infos.Add(new ValidationInfo
            {
                PropertyName = nameof(input.GracePeriodYears),
                InfoMessage = "寬限期較長可能增加總利息負擔",
                RecommendationMessage = "請評估寬限期結束後的還款能力"
            });
        }

        result.IsValid = !result.Errors.Any();
        return result;
    }

    public List<string> GetCorrectionSuggestions(LoanCalculationInput input)
    {
        var suggestions = new List<string>();

        // 貸款條件建議
        var maxLoanAmount = input.HousePrice - input.DownPayment;
        if (input.CalculatedLoanAmount > maxLoanAmount)
        {
            suggestions.Add($"建議調整為最大可貸金額：{maxLoanAmount:N0} 元");
        }

        // 貸款成數建議
        if (input.CalculatedLoanRatio > 85)
        {
            var suggestedDownPayment = input.HousePrice * 0.25m; // 建議自備款25%
            suggestions.Add($"建議增加自備款至 {suggestedDownPayment:N0} 元 (25%)");
        }

        // 利率建議
        if (input.FixedAnnualRate > 5)
        {
            suggestions.Add("利率偏高，建議比較不同銀行的利率方案");
        }

        // 年限建議
        if (input.LoanYears > 30)
        {
            suggestions.Add("貸款年限較長將增加總利息負擔，請評估是否縮短年限");
        }

        return suggestions;
    }

    public ValidationResult CheckFinancialReasonableness(LoanCalculationInput input)
    {
        var result = new ValidationResult { IsValid = true };

        // 總投資成本檢查
        if (input.TotalInvestmentCost > 100_000_000) // 1億
        {
            result.Warnings.Add(new ValidationWarning
            {
                PropertyName = nameof(input.TotalInvestmentCost),
                WarningMessage = "總投資成本偏高，請確認是否合理",
                SuggestionMessage = "建議重新檢視各項費用設定"
            });
        }

        // 裝潢費用比例檢查
        var renovationRatio = input.RenovationFees / input.HousePrice * 100;
        if (renovationRatio > 30)
        {
            result.Warnings.Add(new ValidationWarning
            {
                PropertyName = nameof(input.RenovationFees),
                WarningMessage = $"裝潢費用占房價比例偏高 ({renovationRatio:F1}%)",
                SuggestionMessage = "一般建議裝潢費用不超過房價20-30%"
            });
        }

        // 自備款比例提醒
        var downPaymentRatio = input.DownPayment / input.HousePrice * 100;
        if (downPaymentRatio < 20)
        {
            result.Infos.Add(new ValidationInfo
            {
                PropertyName = nameof(input.DownPayment),
                InfoMessage = $"自備款比例 ({downPaymentRatio:F1}%) 低於建議值",
                RecommendationMessage = "建議自備款至少為房價20%以獲得較好的貸款條件"
            });
        }

        // 利率合理性檢查
        if (input.FixedAnnualRate < 1)
        {
            result.Infos.Add(new ValidationInfo
            {
                PropertyName = nameof(input.FixedAnnualRate),
                InfoMessage = "利率偏低，請確認是否為優惠利率",
                RecommendationMessage = "優惠利率通常有期限限制，請注意後續調整"
            });
        }

        return result;
    }

    private void ValidateBasicRanges(LoanCalculationInput input, ValidationResult result)
    {
        // 房屋總價
        if (input.HousePrice < 1_000_000 || input.HousePrice > 100_000_000)
        {
            result.Errors.Add(new ValidationError
            {
                PropertyName = nameof(input.HousePrice),
                ErrorMessage = "房屋總價須介於100萬至1億新台幣之間",
                Severity = ValidationSeverity.Error
            });
        }

        // 自備款
        if (input.DownPayment < 0)
        {
            result.Errors.Add(new ValidationError
            {
                PropertyName = nameof(input.DownPayment),
                ErrorMessage = "自備款不可為負數",
                Severity = ValidationSeverity.Error
            });
        }

        // 貸款年限
        if (input.LoanYears < 1 || input.LoanYears > 40)
        {
            result.Errors.Add(new ValidationError
            {
                PropertyName = nameof(input.LoanYears),
                ErrorMessage = "貸款年限須介於1年至40年之間",
                Severity = ValidationSeverity.Error
            });
        }

        // 固定利率
        if (input.IsFixedRateMode && (input.FixedAnnualRate < 0.1m || input.FixedAnnualRate > 20m))
        {
            result.Errors.Add(new ValidationError
            {
                PropertyName = nameof(input.FixedAnnualRate),
                ErrorMessage = "年利率須介於0.1%至20%之間",
                Severity = ValidationSeverity.Error
            });
        }

        // 寬限期
        if (!input.IsNoGracePeriodMode && (input.GracePeriodYears < 1 || input.GracePeriodYears > 5))
        {
            result.Errors.Add(new ValidationError
            {
                PropertyName = nameof(input.GracePeriodYears),
                ErrorMessage = "寬限期須介於1年至5年之間",
                Severity = ValidationSeverity.Error
            });
        }

        // 雜支費用
        if (input.MiscellaneousFees < 0 || input.MiscellaneousFees > 10_000_000)
        {
            result.Errors.Add(new ValidationError
            {
                PropertyName = nameof(input.MiscellaneousFees),
                ErrorMessage = "雜支費用須介於0至1000萬新台幣之間",
                Severity = ValidationSeverity.Error
            });
        }

        // 裝潢費用
        if (input.RenovationFees < 0 || input.RenovationFees > 50_000_000)
        {
            result.Errors.Add(new ValidationError
            {
                PropertyName = nameof(input.RenovationFees),
                ErrorMessage = "裝潢費用須介於0至5000萬新台幣之間",
                Severity = ValidationSeverity.Error
            });
        }
    }
}
