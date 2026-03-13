using System;
using System.Collections.Generic;

namespace LakskongenSim.Logistics
{
    public class Inventory
    {
        public string Item { get; }
        public double StockKg { get; private set; }
        public double ReorderThresholdKg { get; }
        public double ReorderAmountKg { get; }
        public Supplier PreferredSupplier { get; }

        public Inventory(string item, double initialStockKg, double reorderThresholdKg, double reorderAmountKg, Supplier preferredSupplier)
        {
            Item = item;
            StockKg = initialStockKg;
            ReorderThresholdKg = reorderThresholdKg;
            ReorderAmountKg = reorderAmountKg;
            PreferredSupplier = preferredSupplier;
        }

        public void Consume(double kg)
        {
            StockKg = Math.Max(0, StockKg - kg);
        }

        public bool NeedsReorder() => StockKg <= ReorderThresholdKg;

        public Order? CreateReorder(int currentDay)
        {
            if (!NeedsReorder()) return null;
            var deliveryDay = currentDay + PreferredSupplier.LeadTimeDays;
            return new Order(PreferredSupplier.Name, Item, ReorderAmountKg, PreferredSupplier.PricePerKg, deliveryDay);
        }

        public void Receive(double kg)
        {
            StockKg += kg;
        }
    }

    public class Order
    {
        public string SupplierName { get; }
        public string Item { get; }
        public double QuantityKg { get; }
        public double UnitPrice { get; }
        public int ExpectedDeliveryDay { get; }

        public Order(string supplierName, string item, double quantityKg, double unitPrice, int expectedDeliveryDay)
        {
            SupplierName = supplierName;
            Item = item;
            QuantityKg = quantityKg;
            UnitPrice = unitPrice;
            ExpectedDeliveryDay = expectedDeliveryDay;
        }
    }
}