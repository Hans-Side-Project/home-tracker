using HouseTrackerApp.Models;

namespace HouseTrackerApp.Services;

public class LoanCalculationService : ILoanCalculationService
{
    public LoanCalculationResult Calculate(LoanCalculationInput input)
    {
        var result = new LoanCalculationResult
        {
            LoanAmount = input.CalculatedLoanAmount,
            InitialCash = input.InitialCash,
            TotalInvestmentCost = input.TotalInvestmentCost
        };

        // 如果有寬限期，先計算寬限期結果
        if (!input.IsNoGracePeriodMode)
        {
            result.GracePeriodResult = CalculateGracePeriodResult(input);
        }

        // 計算詳細還款計劃
        result.PaymentSchedule = CalculateDetailedSchedule(input);

        // 計算總額
        result.TotalPayment = result.PaymentSchedule.Sum(p => p.Payment);
        result.TotalInterest = result.PaymentSchedule.Sum(p => p.Interest);
        result.MonthlyPayment = CalculateAverageMonthlyPayment(result.PaymentSchedule);

        // 產生期間摘要
        result.PaymentSummaries = GeneratePaymentSummaries(result.PaymentSchedule);

        // 分析利息與本金
        result.Analysis = AnalyzeInterestPrincipal(result.PaymentSchedule);

        return result;
    }

    public decimal CalculateEqualInstallment(decimal principal, decimal monthlyRate, int periods)
    {
        if (monthlyRate == 0) return principal / periods;
        if (periods <= 0) return 0;

        var factor = Math.Pow((double)(1 + monthlyRate), periods);
        return principal * monthlyRate * (decimal)factor / ((decimal)factor - 1);
    }

    public List<MonthlyPaymentDetail> CalculateEqualPrincipal(decimal principal, decimal monthlyRate, int periods)
    {
        var monthlyPrincipal = principal / periods;
        var schedule = new List<MonthlyPaymentDetail>();
        var remainingBalance = principal;
        var cumulativePayment = 0m;
        var cumulativePrincipal = 0m;
        var cumulativeInterest = 0m;

        for (int period = 1; period <= periods; period++)
        {
            var monthlyInterest = remainingBalance * monthlyRate;
            var monthlyPayment = monthlyPrincipal + monthlyInterest;

            cumulativePayment += monthlyPayment;
            cumulativePrincipal += monthlyPrincipal;
            cumulativeInterest += monthlyInterest;
            remainingBalance -= monthlyPrincipal;

            schedule.Add(new MonthlyPaymentDetail
            {
                Period = period,
                Year = (period - 1) / 12 + 1,
                Month = (period - 1) % 12 + 1,
                Principal = monthlyPrincipal,
                Interest = monthlyInterest,
                Payment = monthlyPayment,
                RemainingBalance = Math.Max(0, remainingBalance),
                CumulativePayment = cumulativePayment,
                CumulativePrincipal = cumulativePrincipal,
                CumulativeInterest = cumulativeInterest,
                CurrentRate = monthlyRate,
                IsGracePeriod = false
            });
        }

        return schedule;
    }

    public GracePeriodResult? CalculateGracePeriodResult(LoanCalculationInput input)
    {
        if (input.IsNoGracePeriodMode) return null;

        var gracePeriodMonths = input.GracePeriodMonths;
        var loanAmount = input.CalculatedLoanAmount;
        var gracePeriodRate = input.IsFixedRateMode 
            ? input.MonthlyFixedRate 
            : input.InterestRateStages.First().MonthlyRate;

        // 寬限期每月利息
        var gracePeriodMonthlyPayment = loanAmount * gracePeriodRate;
        var gracePeriodTotalInterest = gracePeriodMonthlyPayment * gracePeriodMonths;

        // 寬限期後還款計算
        var repaymentMonths = input.RepaymentMonths;
        var monthlyPaymentAfterGrace = input.IsFixedRateMode
            ? CalculateEqualInstallment(loanAmount, input.MonthlyFixedRate, repaymentMonths)
            : CalculateStageRatesMonthlyPayment(loanAmount, input.InterestRateStages, repaymentMonths);

        var totalInterestAfterGrace = CalculateTotalInterestAfterGrace(input, loanAmount, repaymentMonths);

        // 比較無寬限期的月付金
        var monthlyPaymentWithoutGrace = input.IsFixedRateMode
            ? CalculateEqualInstallment(loanAmount, input.MonthlyFixedRate, input.LoanYears * 12)
            : CalculateStageRatesMonthlyPayment(loanAmount, input.InterestRateStages, input.LoanYears * 12);

        var paymentIncreaseAmount = monthlyPaymentAfterGrace - gracePeriodMonthlyPayment;
        var paymentIncreasePercentage = gracePeriodMonthlyPayment > 0 
            ? (paymentIncreaseAmount / gracePeriodMonthlyPayment) * 100 
            : 0;

        // 計算總利息增加金額
        var totalInterestWithoutGrace = CalculateTotalInterestWithoutGrace(input, loanAmount);
        var totalInterestIncrease = (gracePeriodTotalInterest + totalInterestAfterGrace) - totalInterestWithoutGrace;

        return new GracePeriodResult
        {
            GracePeriodMonths = gracePeriodMonths,
            GracePeriodMonthlyPayment = gracePeriodMonthlyPayment,
            GracePeriodTotalInterest = gracePeriodTotalInterest,
            RemainingPrincipalAfterGrace = loanAmount,
            RepaymentMonthsAfterGrace = repaymentMonths,
            MonthlyPaymentAfterGrace = monthlyPaymentAfterGrace,
            TotalInterestAfterGrace = totalInterestAfterGrace,
            PaymentIncreaseAmount = paymentIncreaseAmount,
            PaymentIncreasePercentage = paymentIncreasePercentage,
            TotalInterestIncrease = totalInterestIncrease,
            TotalRepaymentPeriod = gracePeriodMonths + repaymentMonths
        };
    }

    public List<MonthlyPaymentDetail> CalculateStageRates(decimal principal, List<InterestRateStage> stages, PaymentMethod paymentMethod)
    {
        var schedule = new List<MonthlyPaymentDetail>();
        var remainingBalance = principal;
        var period = 1;
        var cumulativePayment = 0m;
        var cumulativePrincipal = 0m;
        var cumulativeInterest = 0m;

        foreach (var stage in stages)
        {
            var stageMonths = stage.Months;
            var monthlyRate = stage.MonthlyRate;

            List<MonthlyPaymentDetail> stageSchedule;

            if (paymentMethod == PaymentMethod.EqualInstallment)
            {
                var monthlyPayment = CalculateEqualInstallment(remainingBalance, monthlyRate, stageMonths);
                stageSchedule = CalculateEqualInstallmentStage(remainingBalance, monthlyRate, stageMonths, monthlyPayment, period);
            }
            else
            {
                stageSchedule = CalculateEqualPrincipalStage(remainingBalance, monthlyRate, stageMonths, period);
            }

            // 更新累計值
            foreach (var detail in stageSchedule)
            {
                cumulativePayment += detail.Payment;
                cumulativePrincipal += detail.Principal;
                cumulativeInterest += detail.Interest;

                detail.CumulativePayment = cumulativePayment;
                detail.CumulativePrincipal = cumulativePrincipal;
                detail.CumulativeInterest = cumulativeInterest;
                detail.CurrentRate = monthlyRate;
            }

            schedule.AddRange(stageSchedule);
            
            if (stageSchedule.Any())
            {
                remainingBalance = stageSchedule.Last().RemainingBalance;
                period = stageSchedule.Last().Period + 1;
            }
        }

        return schedule;
    }

    public List<PaymentPeriodSummary> GeneratePaymentSummaries(List<MonthlyPaymentDetail> paymentSchedule)
    {
        var summaries = new List<PaymentPeriodSummary>();
        var maxYear = paymentSchedule.Max(p => p.Year);

        for (int startYear = 1; startYear <= maxYear; startYear += 3)
        {
            var endYear = Math.Min(startYear + 2, maxYear);
            var periodPayments = paymentSchedule.Where(p => p.Year >= startYear && p.Year <= endYear).ToList();

            if (!periodPayments.Any()) continue;

            var summary = new PaymentPeriodSummary
            {
                PeriodDescription = startYear == endYear ? $"第{startYear}年" : $"第{startYear}-{endYear}年",
                StartYear = startYear,
                EndYear = endYear,
                MonthlyPayment = periodPayments.Average(p => p.Payment),
                MonthlyPrincipal = periodPayments.Average(p => p.Principal),
                MonthlyInterest = periodPayments.Average(p => p.Interest),
                PeriodTotalPayment = periodPayments.Sum(p => p.Payment),
                PeriodTotalPrincipal = periodPayments.Sum(p => p.Principal),
                PeriodTotalInterest = periodPayments.Sum(p => p.Interest),
                RemainingBalance = periodPayments.Last().RemainingBalance
            };

            // 計算本息比例
            if (summary.PeriodTotalPayment > 0)
            {
                summary.PrincipalPercentage = summary.PeriodTotalPrincipal / summary.PeriodTotalPayment * 100;
                summary.InterestPercentage = summary.PeriodTotalInterest / summary.PeriodTotalPayment * 100;
            }

            summaries.Add(summary);
        }

        return summaries;
    }

    public InterestPrincipalAnalysis AnalyzeInterestPrincipal(List<MonthlyPaymentDetail> paymentSchedule)
    {
        var totalPrincipal = paymentSchedule.Sum(p => p.Principal);
        var totalInterest = paymentSchedule.Sum(p => p.Interest);
        var totalPayment = totalPrincipal + totalInterest;

        var analysis = new InterestPrincipalAnalysis
        {
            TotalPrincipalPayment = totalPrincipal,
            TotalInterestPayment = totalInterest,
            PrincipalPercentage = totalPayment > 0 ? totalPrincipal / totalPayment * 100 : 0,
            InterestPercentage = totalPayment > 0 ? totalInterest / totalPayment * 100 : 0
        };

        // 年度分析
        var yearlyGroups = paymentSchedule.GroupBy(p => p.Year).OrderBy(g => g.Key);
        foreach (var yearGroup in yearlyGroups)
        {
            var yearlyPayments = yearGroup.ToList();
            var yearlyPayment = yearlyPayments.Sum(p => p.Payment);
            var yearlyPrincipal = yearlyPayments.Sum(p => p.Principal);
            var yearlyInterest = yearlyPayments.Sum(p => p.Interest);

            analysis.YearlyBreakdown.Add(new YearlyAnalysis
            {
                Year = yearGroup.Key,
                YearlyPayment = yearlyPayment,
                YearlyPrincipal = yearlyPrincipal,
                YearlyInterest = yearlyInterest,
                YearlyPrincipalPercentage = yearlyPayment > 0 ? yearlyPrincipal / yearlyPayment * 100 : 0,
                YearlyInterestPercentage = yearlyPayment > 0 ? yearlyInterest / yearlyPayment * 100 : 0,
                YearEndBalance = yearlyPayments.Last().RemainingBalance
            });
        }

        return analysis;
    }

    private List<MonthlyPaymentDetail> CalculateDetailedSchedule(LoanCalculationInput input)
    {
        var schedule = new List<MonthlyPaymentDetail>();
        var loanAmount = input.CalculatedLoanAmount;

        // 寬限期計算
        if (!input.IsNoGracePeriodMode)
        {
            var graceSchedule = CalculateGracePeriodSchedule(input);
            schedule.AddRange(graceSchedule);
        }

        // 正常還款期計算
        var repaymentSchedule = CalculateRepaymentSchedule(input);
        
        // 調整期數編號
        var startPeriod = schedule.Count + 1;
        foreach (var payment in repaymentSchedule)
        {
            payment.Period = startPeriod++;
            payment.Year = (payment.Period - 1) / 12 + 1;
            payment.Month = (payment.Period - 1) % 12 + 1;
        }

        schedule.AddRange(repaymentSchedule);

        return schedule;
    }

    private List<MonthlyPaymentDetail> CalculateGracePeriodSchedule(LoanCalculationInput input)
    {
        var schedule = new List<MonthlyPaymentDetail>();
        var loanAmount = input.CalculatedLoanAmount;
        var gracePeriodMonths = input.GracePeriodMonths;
        var monthlyRate = input.IsFixedRateMode 
            ? input.MonthlyFixedRate 
            : input.InterestRateStages.First().MonthlyRate;

        var monthlyInterest = loanAmount * monthlyRate;
        var cumulativePayment = 0m;
        var cumulativeInterest = 0m;

        for (int period = 1; period <= gracePeriodMonths; period++)
        {
            cumulativePayment += monthlyInterest;
            cumulativeInterest += monthlyInterest;

            schedule.Add(new MonthlyPaymentDetail
            {
                Period = period,
                Year = (period - 1) / 12 + 1,
                Month = (period - 1) % 12 + 1,
                Payment = monthlyInterest,
                Principal = 0,
                Interest = monthlyInterest,
                RemainingBalance = loanAmount,
                CumulativePayment = cumulativePayment,
                CumulativePrincipal = 0,
                CumulativeInterest = cumulativeInterest,
                CurrentRate = monthlyRate,
                IsGracePeriod = true
            });
        }

        return schedule;
    }

    private List<MonthlyPaymentDetail> CalculateRepaymentSchedule(LoanCalculationInput input)
    {
        var loanAmount = input.CalculatedLoanAmount;
        var repaymentMonths = input.RepaymentMonths;

        if (input.IsFixedRateMode)
        {
            var monthlyRate = input.MonthlyFixedRate;
            
            if (input.PaymentMethod == PaymentMethod.EqualInstallment)
            {
                var monthlyPayment = CalculateEqualInstallment(loanAmount, monthlyRate, repaymentMonths);
                return CalculateEqualInstallmentStage(loanAmount, monthlyRate, repaymentMonths, monthlyPayment, 1);
            }
            else
            {
                return CalculateEqualPrincipal(loanAmount, monthlyRate, repaymentMonths);
            }
        }
        else
        {
            return CalculateStageRates(loanAmount, input.InterestRateStages, input.PaymentMethod);
        }
    }

    private List<MonthlyPaymentDetail> CalculateEqualInstallmentStage(decimal principal, decimal monthlyRate, int periods, decimal monthlyPayment, int startPeriod)
    {
        var schedule = new List<MonthlyPaymentDetail>();
        var remainingBalance = principal;

        for (int i = 0; i < periods; i++)
        {
            var monthlyInterest = remainingBalance * monthlyRate;
            var monthlyPrincipal = monthlyPayment - monthlyInterest;
            
            // 最後一期調整
            if (i == periods - 1)
            {
                monthlyPrincipal = remainingBalance;
                monthlyPayment = monthlyPrincipal + monthlyInterest;
            }

            remainingBalance -= monthlyPrincipal;

            schedule.Add(new MonthlyPaymentDetail
            {
                Period = startPeriod + i,
                Year = (startPeriod + i - 1) / 12 + 1,
                Month = (startPeriod + i - 1) % 12 + 1,
                Payment = monthlyPayment,
                Principal = monthlyPrincipal,
                Interest = monthlyInterest,
                RemainingBalance = Math.Max(0, remainingBalance),
                CurrentRate = monthlyRate,
                IsGracePeriod = false
            });
        }

        return schedule;
    }

    private List<MonthlyPaymentDetail> CalculateEqualPrincipalStage(decimal principal, decimal monthlyRate, int periods, int startPeriod)
    {
        var monthlyPrincipal = principal / periods;
        var schedule = new List<MonthlyPaymentDetail>();
        var remainingBalance = principal;

        for (int i = 0; i < periods; i++)
        {
            var monthlyInterest = remainingBalance * monthlyRate;
            var monthlyPayment = monthlyPrincipal + monthlyInterest;

            remainingBalance -= monthlyPrincipal;

            schedule.Add(new MonthlyPaymentDetail
            {
                Period = startPeriod + i,
                Year = (startPeriod + i - 1) / 12 + 1,
                Month = (startPeriod + i - 1) % 12 + 1,
                Payment = monthlyPayment,
                Principal = monthlyPrincipal,
                Interest = monthlyInterest,
                RemainingBalance = Math.Max(0, remainingBalance),
                CurrentRate = monthlyRate,
                IsGracePeriod = false
            });
        }

        return schedule;
    }

    private decimal CalculateAverageMonthlyPayment(List<MonthlyPaymentDetail> schedule)
    {
        var normalPayments = schedule.Where(p => !p.IsGracePeriod).ToList();
        return normalPayments.Any() ? normalPayments.Average(p => p.Payment) : 0;
    }

    private decimal CalculateStageRatesMonthlyPayment(decimal principal, List<InterestRateStage> stages, int totalMonths)
    {
        // 這是簡化計算，實際應該用更精確的方法
        var weightedRate = stages.Sum(s => s.MonthlyRate * s.Months) / stages.Sum(s => s.Months);
        return CalculateEqualInstallment(principal, weightedRate, totalMonths);
    }

    private decimal CalculateTotalInterestAfterGrace(LoanCalculationInput input, decimal loanAmount, int repaymentMonths)
    {
        if (input.IsFixedRateMode)
        {
            var monthlyPayment = CalculateEqualInstallment(loanAmount, input.MonthlyFixedRate, repaymentMonths);
            return monthlyPayment * repaymentMonths - loanAmount;
        }
        else
        {
            var schedule = CalculateStageRates(loanAmount, input.InterestRateStages, input.PaymentMethod);
            return schedule.Sum(p => p.Interest);
        }
    }

    private decimal CalculateTotalInterestWithoutGrace(LoanCalculationInput input, decimal loanAmount)
    {
        var totalMonths = input.LoanYears * 12;
        
        if (input.IsFixedRateMode)
        {
            var monthlyPayment = CalculateEqualInstallment(loanAmount, input.MonthlyFixedRate, totalMonths);
            return monthlyPayment * totalMonths - loanAmount;
        }
        else
        {
            var schedule = CalculateStageRates(loanAmount, input.InterestRateStages, input.PaymentMethod);
            return schedule.Sum(p => p.Interest);
        }
    }
}
