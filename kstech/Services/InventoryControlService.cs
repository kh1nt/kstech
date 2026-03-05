using kstech.Data;
using kstech.Models.Entities;

namespace kstech.Services
{
    public interface IInventoryControlService
    {
        void ApplyStockIn(
            Product product,
            int quantity,
            decimal unitCost,
            string supplierName,
            string reason,
            string referenceType,
            string referenceId,
            int? performedByUserId = null);

        void ApplyStockOut(
            Product product,
            int quantity,
            string reason,
            string referenceType,
            string referenceId,
            int? performedByUserId = null);
    }

    public class InventoryControlService : IInventoryControlService
    {
        private readonly ApplicationDbContext _context;
        public InventoryControlService(ApplicationDbContext context)
        {
            _context = context;
        }

        public void ApplyStockIn(
            Product product,
            int quantity,
            decimal unitCost,
            string supplierName,
            string reason,
            string referenceType,
            string referenceId,
            int? performedByUserId = null)
        {
            if (quantity <= 0)
            {
                return;
            }

            var beforeQuantity = product.StockQuantity;
            product.StockQuantity += quantity;
            product.CostPrice = unitCost > 0m ? unitCost : product.CostPrice;

            _context.InventoryMovements.Add(new InventoryMovement
            {
                ProductID = product.ProductID,
                OwnerUserID = product.OwnerUserID,
                MovementType = "StockIn",
                QuantityDelta = quantity,
                QuantityBefore = beforeQuantity,
                QuantityAfter = product.StockQuantity,
                UnitCostAtMovement = product.CostPrice,
                PartnerName = supplierName ?? string.Empty,
                Reason = reason ?? string.Empty,
                ReferenceType = referenceType ?? string.Empty,
                ReferenceId = referenceId ?? string.Empty,
                PerformedByUserID = performedByUserId,
                OccurredAtUtc = DateTime.UtcNow
            });
        }

        public void ApplyStockOut(
            Product product,
            int quantity,
            string reason,
            string referenceType,
            string referenceId,
            int? performedByUserId = null)
        {
            if (quantity <= 0)
            {
                return;
            }

            var beforeQuantity = product.StockQuantity;
            var afterQuantity = Math.Max(0, beforeQuantity - quantity);
            product.StockQuantity = afterQuantity;

            _context.InventoryMovements.Add(new InventoryMovement
            {
                ProductID = product.ProductID,
                OwnerUserID = product.OwnerUserID,
                MovementType = "StockOut",
                QuantityDelta = -quantity,
                QuantityBefore = beforeQuantity,
                QuantityAfter = afterQuantity,
                UnitCostAtMovement = product.CostPrice,
                Reason = reason ?? string.Empty,
                ReferenceType = referenceType ?? string.Empty,
                ReferenceId = referenceId ?? string.Empty,
                PerformedByUserID = performedByUserId,
                OccurredAtUtc = DateTime.UtcNow
            });
        }

    }
}
