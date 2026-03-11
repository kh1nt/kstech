using System.Linq.Expressions;
using kstech.Models.Entities;

namespace kstech.Utilities
{
    public static class RevenueRecognitionPolicy
    {
        public const string PaidPaymentStatus = "Paid";
        public const string RefundedPaymentStatus = "Refunded";
        public const string CancelledOrderStatus = "Cancelled";

        private static readonly Expression<Func<Order, bool>> RecognizedRevenueOrderPredicate = order =>
            order.PaymentStatus == PaidPaymentStatus &&
            order.PaymentStatus != RefundedPaymentStatus &&
            order.OrderStatus != CancelledOrderStatus;

        private static readonly Expression<Func<OrderDetail, bool>> RecognizedRevenueOrderDetailPredicate = detail =>
            detail.Order != null &&
            detail.Order.PaymentStatus == PaidPaymentStatus &&
            detail.Order.PaymentStatus != RefundedPaymentStatus &&
            detail.Order.OrderStatus != CancelledOrderStatus;

        public static IQueryable<Order> ApplyRecognizedRevenueOrderFilter(IQueryable<Order> query)
        {
            return query.Where(RecognizedRevenueOrderPredicate);
        }

        public static IQueryable<OrderDetail> ApplyRecognizedRevenueOrderDetailFilter(IQueryable<OrderDetail> query)
        {
            return query.Where(RecognizedRevenueOrderDetailPredicate);
        }

        public static bool IsRecognizedRevenueOrder(Order order)
        {
            return IsRecognizedRevenueStatus(order.PaymentStatus, order.OrderStatus);
        }

        public static bool IsRecognizedRevenueStatus(string? paymentStatus, string? orderStatus)
        {
            return string.Equals(paymentStatus, PaidPaymentStatus, StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(paymentStatus, RefundedPaymentStatus, StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(orderStatus, CancelledOrderStatus, StringComparison.OrdinalIgnoreCase);
        }
    }
}
