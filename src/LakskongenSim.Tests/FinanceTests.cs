using System;
using Xunit;
using LakskongenSim.Finance;

namespace LakskongenSim.Tests
{
    public class FinanceTests
    {
        [Fact]
        public void FinancialStatements_BasicFlow_Works()
        {
            var fs = new FinancialStatements(startingCash:100000m, startingAssets:200000m, startingLiabilities:50000m);
            fs.RecordRevenue(50000m);
            fs.RecordCOGS(20000m);
            fs.RecordOperatingExpense(5000m);
            Assert.Equal(50000m + 100000m - 20000m - 5000m, fs.Cash);
            var net = fs.ComputeNetIncome();
            Assert.Equal(50000m - 20000m - 5000m - 0m - 0m, net);
        }

        [Fact]
        public void LoanSystem_Interest_Calculation()
        {
            var loan = new LoanSystem();
            loan.InterestRateAnnual = 0.1m;
            var interest = loan.ChargeInterest(100000m, 30);
            var expected = 100000m * 0.1m * (30m/365m);
            Assert.Equal(Math.Round(expected,10), Math.Round(interest,10));
        }

        [Fact]
        public void Investment_Depreciation_ReducesValue()
        {
            var inv = new InvestmentSystem();
            inv.InvestInEquipment(100000m);
            var dep = inv.DepreciateEquipment(0.1m, 365);
            Assert.Equal(10000m, Math.Round(dep,2));
            Assert.Equal(90000m, Math.Round(inv.EquipmentValue,2));
        }

        [Fact]
        public void Insurance_Premium_Computation()
        {
            var ins = new InsuranceSystem();
            ins.Insure(1000000m, 0.02m);
            var premium = ins.ComputePremium(365);
            Assert.Equal(20000m, premium);
        }

        [Fact]
        public void Investor_IssueEquity_IncreasesShares()
        {
            var inv = new InvestorSystem();
            var shares = inv.IssueEquity(100000m, 10m);
            Assert.Equal(10000m, shares);
            Assert.True(inv.OutstandingShares > 1_000_000m);
        }
    }
}
