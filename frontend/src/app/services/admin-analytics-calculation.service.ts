import { Injectable } from '@angular/core';
import {
  convertToIst,
  formatIstDate,
  getIstInputDate,
  getIstStartOfDay,
  getIstEndOfDay,
  getIstDaysDifference,
} from '../utils/date-utils';

export interface DateRange {
  startDate: string;
  endDate: string;
}

@Injectable({ providedIn: 'root' })
export class AdminAnalyticsCalculationService {

  filterByDateRange(sales: any[], dateRange: DateRange): any[] {
    const start = getIstStartOfDay(dateRange.startDate);
    const end = getIstEndOfDay(dateRange.endDate);
    return sales.filter((sale) => {
      const saleDate = new Date(sale.date);
      return saleDate >= start && saleDate <= end;
    });
  }

  calculateWeeklyTrend(salesData: any[]): { week: string; total: number }[] {
    const weeks = new Map<string, number>();
    salesData.forEach((sale) => {
      const date = convertToIst(new Date(sale.date));
      const weekStart = new Date(date);
      weekStart.setDate(date.getDate() - date.getDay());
      const weekKey = getIstInputDate(weekStart);
      weeks.set(weekKey, (weeks.get(weekKey) || 0) + sale.totalAmount);
    });
    return Array.from(weeks.entries())
      .map(([week, total]) => ({ week: formatIstDate(new Date(week)), total }))
      .sort((a, b) => new Date(b.week).getTime() - new Date(a.week).getTime())
      .slice(0, 4);
  }

  calculateMonthlyComparison(salesData: any[]): { month: string; total: number; transactions: number }[] {
    const months = new Map<string, { total: number; transactions: number }>();
    salesData.forEach((sale) => {
      const date = convertToIst(new Date(sale.date));
      const monthKey = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
      const existing = months.get(monthKey) || { total: 0, transactions: 0 };
      months.set(monthKey, {
        total: existing.total + sale.totalAmount,
        transactions: existing.transactions + 1,
      });
    });
    return Array.from(months.entries())
      .map(([month, data]) => ({
        month: formatIstDate(new Date(month + '-01'), { month: 'short', year: 'numeric' }),
        ...data,
      }))
      .sort((a, b) => b.month.localeCompare(a.month))
      .slice(0, 6)
      .reverse();
  }

  calculatePeakDays(salesData: any[]): { day: string; total: number }[] {
    const days = new Map<string, number>();
    salesData.forEach((sale) => {
      const day = sale.date.split('T')[0];
      days.set(day, (days.get(day) || 0) + sale.totalAmount);
    });
    return Array.from(days.entries())
      .map(([day, total]) => ({ day: formatIstDate(new Date(day)), total }))
      .sort((a, b) => b.total - a.total)
      .slice(0, 5);
  }

  calculateGrowthRate(salesData: any[], allSalesData: any[], dateRange: DateRange): number {
    const currentPeriodTotal = salesData.reduce((sum, sale) => sum + sale.totalAmount, 0);
    const startDate = getIstStartOfDay(dateRange.startDate);
    const endDate = getIstEndOfDay(dateRange.endDate);
    const daysDiff = Math.floor((endDate.getTime() - startDate.getTime()) / (1000 * 60 * 60 * 24));

    const prevStartDate = new Date(startDate);
    prevStartDate.setMonth(prevStartDate.getMonth() - 1);
    const prevEndDate = new Date(prevStartDate);
    prevEndDate.setDate(prevEndDate.getDate() + daysDiff);

    const prevPeriodSales = allSalesData.filter((sale) => {
      const saleDate = new Date(sale.date);
      return saleDate >= prevStartDate && saleDate <= prevEndDate;
    });
    const prevPeriodTotal = prevPeriodSales.reduce((sum, sale) => sum + sale.totalAmount, 0);

    if (prevPeriodTotal === 0) {
      return currentPeriodTotal > 0 ? 100 : 0;
    }
    return ((currentPeriodTotal - prevPeriodTotal) / prevPeriodTotal) * 100;
  }

  calculateCategoryBreakdown(salesData: any[], categorizeItem: (name: string) => string): { category: string; count: number; revenue: number }[] {
    const categories = new Map<string, { count: number; revenue: number }>();
    salesData.forEach((sale) => {
      sale.items.forEach((item: any) => {
        const category = categorizeItem(item.itemName);
        const existing = categories.get(category) || { count: 0, revenue: 0 };
        categories.set(category, {
          count: existing.count + item.quantity,
          revenue: existing.revenue + item.totalPrice,
        });
      });
    });
    return Array.from(categories.entries())
      .map(([category, data]) => ({ category, ...data }))
      .sort((a, b) => b.revenue - a.revenue);
  }

  categorizeItem(itemName: string): string {
    if (itemName.includes('Tea -')) return 'Tea Variants';
    if (itemName.toLowerCase().includes('tea')) return 'Tea Products';
    if (itemName.toLowerCase().includes('coffee')) return 'Coffee';
    if (itemName.toLowerCase().includes('biscuit') || itemName.toLowerCase().includes('snacks')) return 'Snacks';
    if (itemName.toLowerCase().includes('water') || itemName.toLowerCase().includes('campa')) return 'Beverages';
    if (itemName.toLowerCase().includes('cigarete')) return 'Tobacco';
    return 'Others';
  }

  calculatePlatformMetrics(sales: any[], platform: string, platformCharges: any[], dateRange: DateRange): any {
    if (sales.length === 0) {
      return {
        totalIncome: 0, itemSubtotal: 0, totalOrders: 0, avgIncomePerOrder: 0,
        dailyAverage: 0, avgOrdersPerDay: 0, avgRating: 0, totalDeduction: 0,
        avgDeductionPercent: 0, totalDiscount: 0, avgDiscountPerOrder: 0,
        ordersWithDiscount: 0, discountUsagePercent: 0, totalPackaging: 0,
        avgPackagingPerOrder: 0, ordersWithPackaging: 0, packagingUsagePercent: 0,
        totalFreebies: 0, avgFreebiesPerOrder: 0, ordersWithFreebies: 0,
        freebiesUsagePercent: 0, totalMonthlyCharges: 0, avgDistance: 0,
        minDistance: 0, maxDistance: 0, commonDistanceRange: 'N/A',
        topItems: [], dailyTrend: [], maxDailyIncome: 1, peakDays: [],
        ratingDistribution: [], monthlyData: [],
      };
    }

    const totalOrders = sales.length;
    const totalDeduction = sales.reduce((sum, s) => sum + (s.platformDeduction || 0), 0);
    const itemSubtotal = sales.reduce((sum, s) => sum + (s.billSubTotal || 0), 0);
    const totalPackaging = sales.reduce((sum, s) => sum + (s.packagingCharges || 0), 0);
    const avgPackagingPerOrder = totalPackaging / totalOrders;
    const ordersWithPackaging = sales.filter((s) => (s.packagingCharges || 0) > 0).length;
    const packagingUsagePercent = totalOrders > 0 ? (ordersWithPackaging / totalOrders) * 100 : 0;

    const totalDiscount = sales.reduce((sum, s) => sum + (s.discountAmount || 0), 0);
    const ordersWithDiscount = sales.filter((s) => (s.discountAmount || 0) > 0).length;
    const avgDiscountPerOrder = totalDiscount / totalOrders;
    const discountUsagePercent = totalOrders > 0 ? (ordersWithDiscount / totalOrders) * 100 : 0;

    const totalFreebies = sales.reduce((sum, s) => sum + (s.freebies || 0), 0);
    const ordersWithFreebies = sales.filter((s) => (s.freebies || 0) > 0).length;
    const avgFreebiesPerOrder = totalFreebies / totalOrders;
    const freebiesUsagePercent = totalOrders > 0 ? (ordersWithFreebies / totalOrders) * 100 : 0;

    const startDate = new Date(dateRange.startDate);
    const endDate = new Date(dateRange.endDate);
    const startMonth = startDate.getMonth() + 1;
    const startYear = startDate.getFullYear();
    const endMonth = endDate.getMonth() + 1;
    const endYear = endDate.getFullYear();

    const totalMonthlyCharges = platformCharges
      .filter((pc: any) => {
        if (platform === 'All') {
          if (pc.platform !== 'Zomato' && pc.platform !== 'Swiggy') return false;
        } else {
          if (pc.platform !== platform) return false;
        }
        if (pc.year < startYear || pc.year > endYear) return false;
        if (pc.year === startYear && pc.year === endYear) {
          return pc.month >= startMonth && pc.month <= endMonth;
        }
        if (pc.year === startYear) return pc.month >= startMonth;
        if (pc.year === endYear) return pc.month <= endMonth;
        return true;
      })
      .reduce((sum: number, pc: any) => sum + (pc.charges || 0), 0);

    const totalIncome = itemSubtotal + totalPackaging - totalDiscount - totalDeduction - totalMonthlyCharges;
    const dates = [...new Set(sales.map((s) => new Date(s.orderAt).toDateString()))];
    const daysInRange = dates.length;
    const dailyAverage = totalIncome / Math.max(daysInRange, 1);
    const avgOrdersPerDay = totalOrders / Math.max(daysInRange, 1);
    const avgIncomePerOrder = totalIncome / totalOrders;
    const avgDeductionPercent = totalDeduction > 0 && totalIncome + totalMonthlyCharges > 0
      ? (totalDeduction / (totalIncome + totalMonthlyCharges + totalDeduction)) * 100 : 0;

    const ratingsWithValues = sales.filter((s) => s.rating && s.rating > 0);
    const avgRating = ratingsWithValues.length > 0
      ? ratingsWithValues.reduce((sum, s) => sum + s.rating, 0) / ratingsWithValues.length : 0;

    const salesWithDistance = sales.filter((s) => s.distance && s.distance > 0);
    const avgDistance = salesWithDistance.length > 0
      ? salesWithDistance.reduce((sum, s) => sum + s.distance, 0) / salesWithDistance.length : 0;
    const minDistance = salesWithDistance.length > 0 ? Math.min(...salesWithDistance.map((s) => s.distance)) : 0;
    const maxDistance = salesWithDistance.length > 0 ? Math.max(...salesWithDistance.map((s) => s.distance)) : 0;

    const distanceRanges = salesWithDistance.map((s) => {
      const km = s.distance;
      if (km < 2) return '0-2 km';
      if (km < 5) return '2-5 km';
      if (km < 10) return '5-10 km';
      return '10+ km';
    });
    const rangeCounts = distanceRanges.reduce((acc, range) => {
      acc[range] = (acc[range] || 0) + 1;
      return acc;
    }, {} as any);
    const commonDistanceRange = Object.entries(rangeCounts).sort(
      ([, a]: any, [, b]: any) => b - a
    )[0]?.[0] || 'N/A';

    const itemMap = new Map<string, { count: number; quantity: number }>();
    sales.forEach((sale) => {
      if (sale.orderedItems) {
        try {
          const items = typeof sale.orderedItems === 'string' ? JSON.parse(sale.orderedItems) : sale.orderedItems;
          if (Array.isArray(items)) {
            items.forEach((item: any) => {
              const name = item.name || item.itemName || 'Unknown Item';
              const qty = parseInt(item.quantity || '1');
              const existing = itemMap.get(name) || { count: 0, quantity: 0 };
              itemMap.set(name, { count: existing.count + 1, quantity: existing.quantity + qty });
            });
          }
        } catch (e) {}
      }
    });
    const topItems = Array.from(itemMap.entries())
      .map(([name, data]) => ({ name, ...data }))
      .sort((a, b) => b.count - a.count)
      .slice(0, 10);

    const dailyMap = new Map<string, { income: number; orders: number }>();
    sales.forEach((sale) => {
      const istOrderDate = convertToIst(new Date(sale.orderAt));
      const dateKey = getIstInputDate(istOrderDate);
      const existing = dailyMap.get(dateKey) || { income: 0, orders: 0 };
      const netPayout = (sale.billSubTotal || 0) + (sale.packagingCharges || 0) - (sale.discountAmount || 0) - (sale.platformDeduction || 0);
      dailyMap.set(dateKey, { income: existing.income + netPayout, orders: existing.orders + 1 });
    });
    const dailyTrend = Array.from(dailyMap.entries())
      .map(([date, data]) => ({ date, ...data }))
      .sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime());
    const maxDailyIncome = Math.max(...dailyTrend.map((d) => d.income), 1);

    const dayMap = new Map<number, { income: number; orders: number }>();
    sales.forEach((sale) => {
      const dayOfWeek = new Date(sale.orderAt).getDay();
      const existing = dayMap.get(dayOfWeek) || { income: 0, orders: 0 };
      const grossRevenue = (sale.billSubTotal || 0) + (sale.packagingCharges || 0);
      dayMap.set(dayOfWeek, { income: existing.income + grossRevenue, orders: existing.orders + 1 });
    });
    const dayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
    const peakDays = Array.from(dayMap.entries())
      .map(([day, data]) => ({ dayName: dayNames[day], ...data }))
      .sort((a, b) => b.income - a.income);

    const ratingCounts = [5, 4, 3, 2, 1].map((stars) => {
      const count = sales.filter((s) => s.rating === stars).length;
      const percentage = totalOrders > 0 ? (count / totalOrders) * 100 : 0;
      return { stars, count, percentage };
    });

    const monthlyMap = new Map<string, { income: number; orders: number; ratings: number[] }>();
    sales.forEach((sale) => {
      const date = new Date(sale.orderAt);
      const monthKey = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
      const existing = monthlyMap.get(monthKey) || { income: 0, orders: 0, ratings: [] };
      const netPayout = (sale.billSubTotal || 0) + (sale.packagingCharges || 0) - (sale.discountAmount || 0) - (sale.platformDeduction || 0);
      monthlyMap.set(monthKey, {
        income: existing.income + netPayout,
        orders: existing.orders + 1,
        ratings: sale.rating > 0 ? [...existing.ratings, sale.rating] : existing.ratings,
      });
    });
    const monthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    const monthlyData = Array.from(monthlyMap.entries())
      .map(([key, data]) => {
        const [year, month] = key.split('-');
        const avgRatingVal = data.ratings.length > 0
          ? data.ratings.reduce((sum, r) => sum + r, 0) / data.ratings.length : 0;
        const monthCharges = platformCharges
          .filter((pc: any) => {
            if (platform === 'All') {
              return (pc.platform === 'Zomato' || pc.platform === 'Swiggy') && pc.year === parseInt(year) && pc.month === parseInt(month);
            }
            return pc.platform === platform && pc.year === parseInt(year) && pc.month === parseInt(month);
          })
          .reduce((sum: number, pc: any) => sum + (pc.charges || 0), 0);
        return {
          month: `${monthNames[parseInt(month) - 1]} ${year}`,
          income: data.income - monthCharges,
          orders: data.orders,
          avgRating: avgRatingVal,
        };
      })
      .sort((a, b) => a.month.localeCompare(b.month));

    return {
      totalIncome, itemSubtotal, totalOrders, avgIncomePerOrder,
      dailyAverage, avgOrdersPerDay, avgRating, totalDeduction,
      avgDeductionPercent, totalDiscount, avgDiscountPerOrder,
      ordersWithDiscount, discountUsagePercent, totalPackaging,
      avgPackagingPerOrder, ordersWithPackaging, packagingUsagePercent,
      totalFreebies, avgFreebiesPerOrder, ordersWithFreebies,
      freebiesUsagePercent, totalMonthlyCharges, avgDistance,
      minDistance, maxDistance, commonDistanceRange, topItems,
      dailyTrend, maxDailyIncome, peakDays,
      ratingDistribution: ratingCounts, monthlyData,
    };
  }

  calculateProportionalMonthlyExpense(month: number, year: number, monthlyAmount: number, rangeStart: Date, rangeEnd: Date): number {
    const monthStart = new Date(year, month - 1, 1);
    const monthEnd = new Date(year, month, 0);
    const overlapStart = new Date(Math.max(monthStart.getTime(), rangeStart.getTime()));
    const overlapEnd = new Date(Math.min(monthEnd.getTime(), rangeEnd.getTime()));
    if (overlapStart > overlapEnd) return 0;
    const daysInOverlap = Math.ceil((overlapEnd.getTime() - overlapStart.getTime()) / (1000 * 60 * 60 * 24)) + 1;
    const daysInMonth = monthEnd.getDate();
    return (monthlyAmount / daysInMonth) * daysInOverlap;
  }

  getTopItems(items: string[], limit: number): string[] {
    const itemCounts = items.reduce((acc, item) => {
      acc[item] = (acc[item] || 0) + 1;
      return acc;
    }, {} as any);
    return Object.entries(itemCounts)
      .sort(([, a]: any, [, b]: any) => b - a)
      .slice(0, limit)
      .map(([item]) => item);
  }

  formatCurrency(amount: number): string {
    return `₹${amount.toFixed(2)}`;
  }

  formatPercentage(value: number): string {
    return `${value > 0 ? '+' : ''}${value.toFixed(1)}%`;
  }

  getCategoryIcon(category: string): string {
    const icons: { [key: string]: string } = {
      'Tea Variants': '🍵', 'Tea Products': '☕', Coffee: '☕',
      Snacks: '🍪', Beverages: '🥤', Tobacco: '🚬', Others: '📦',
    };
    return icons[category] || '📦';
  }

  getExpenseTypeIcon(type: string): string {
    const icons: { [key: string]: string } = {
      Milk: '🥛', Tea: '🍵', Rent: '🏠', Salary: '💰',
      Grocerry: '🛒', Electricity: '⚡', Water: '💧',
    };
    return icons[type] || '💸';
  }
}
