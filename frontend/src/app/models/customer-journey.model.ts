export interface JourneyContext { sessionId: string; token: string; }
export interface JourneySessionResponse { context: JourneyContext; expiresAt: string; }
export interface JourneyBrowserEvent {
  id: string;
  type: string;
  occurredAt: string;
  page: string;
  menuItemId?: string;
  categoryId?: string;
  quantity?: number;
  cartCount?: number;
  cartValue?: number;
  deliveryFee?: number;
  activeSeconds?: number;
}
export interface JourneyOverview {
  visitors: number; sessions: number; loggedInVisitors: number; returningVisitors: number;
  pageViews: number; activeSeconds: number; averageActiveSeconds: number; orders: number;
  cancelledOrders: number; revenue: number; conversionRate: number; cartAbandonmentRate: number;
  liveVisitors: number; staffSessions: number;
}
export interface JourneyBreakdown {
  name: string; sessions: number; views: number; orders: number; abandonedCarts: number;
  activeSeconds: number; revenue: number; conversionRate: number;
}
export interface JourneyMenuStat {
  itemId: string; name: string; category: string; views: number; clicks: number; viewers: number;
  cartAdds: number; cartSessions: number; quantityOrdered: number; orders: number; revenue: number;
  addToCartRate: number; orderRate: number; highInterestLowConversion: boolean;
}
export interface JourneySessionSummary {
  id: string; visitorLabel: string; accountId?: string; accountLabel?: string;
  isLoggedIn: boolean; isStaff: boolean; isReturning: boolean; device: string;
  startedAt: string; lastSeenAt: string; lastPage: string; activeSeconds: number;
  cartValue: number; deliveryFee?: number; outcome: string; orders: number; pages: number;
}
export interface JourneyTimelineEvent {
  id: string; type: string; occurredAt: string; page: string; itemName?: string; category?: string;
  quantity: number; cartCount: number; cartValue: number; deliveryFee?: number;
  activeSeconds: number; orderId?: string; cancelled: boolean;
}
export interface JourneySessionList {
  sessions: JourneySessionSummary[]; total: number; page: number; pageSize: number;
}
export interface JourneyTimeline {
  session: JourneySessionSummary; events: JourneyTimelineEvent[]; total: number; page: number; pageSize: number;
  pages: { page: string; views: number; activeSeconds: number }[];
}
export interface JourneyDashboard {
  from: string; toExclusive: string; retainedSince: string; generatedAt: string;
  overview: JourneyOverview; previous?: JourneyOverview;
  daily: { date: string; visitors: number; sessions: number; orders: number }[];
  funnel: { name: string; sessions: number; dropOffRate: number }[];
  pages: JourneyBreakdown[]; categories: JourneyBreakdown[]; visitorTypes: JourneyBreakdown[];
  devices: JourneyBreakdown[]; cartValues: JourneyBreakdown[]; deliveryCharges: JourneyBreakdown[];
  paths: JourneyBreakdown[]; userSegments: JourneyBreakdown[]; menu: JourneyMenuStat[];
  recentSessions: JourneySessionSummary[]; registeredAccountsAllOutlets: number;
}
export interface JourneyLive {
  generatedAt: string; sessions: JourneySessionSummary[]; recentEvents: JourneyTimelineEvent[];
}
