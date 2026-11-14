"use client";

import React, { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Page, PageHeader, Pill, btnOutline, btnSmall, card, muted } from "@/components/ui";
import { Drawer, Field, Pager, QueryState, Select, Table, TextInput, fmtDateTime } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { ArrowLeftRight, Ban, Banknote, CalendarDays, ChevronLeft, CircleCheck, CircleHelp, CirclePlay, CirclePlus, CircleStop, CircleX, ClipboardList, CreditCard, Eye, FileText, Flag, Globe, Info, KeyRound, LifeBuoy, Lock, LockOpen, LogOut, type LucideIcon, Mail, MailPlus, MessageSquare, NotebookPen, Package, Pencil, ReceiptText, RefreshCw, Scale, Search, Settings, ShieldCheck, Star, Target, Ticket, TriangleAlert, User, VenetianMask, Zap } from "lucide-react";
import { Pitch, SoccerBall } from "@/components/icons";

type Row = Schemas["AuditLogDto"];
const PAGE_SIZE = 50;

interface ActionMeta {
  title: string;
  category: string;
  tone: "ok" | "warn" | "bad" | "neutral" | "navy";
  icon: LucideIcon | typeof Pitch;
}

const TONE_ICON: Record<ActionMeta["tone"], string> = {
  ok: "bg-emerald-50 text-emerald-700",
  warn: "bg-amber-50 text-amber-700",
  bad: "bg-red-50 text-red-700",
  neutral: "bg-slate-100 text-slate-600",
  navy: "bg-blue-50 text-blue-800",
};

const ACTION_MAP: Record<string, ActionMeta> = {
  // الاشتراكات وتجديد الباقات
  "subscription.payment_recorded": { title: "تسجيل دفعة اشتراك", category: "اشتراكات", tone: "ok", icon: CreditCard },
  "subscription.assigned": { title: "إسناد باقة لمالك", category: "اشتراكات", tone: "ok", icon: Package },
  "subscription.plan_changed": { title: "تغيير باقة المالك", category: "اشتراكات", tone: "navy", icon: RefreshCw },
  "subscription.suspended": { title: "إيقاف اشتراك مالك", category: "اشتراكات", tone: "bad", icon: CircleStop },
  "subscription.reactivated": { title: "إعادة تنشيط اشتراك", category: "اشتراكات", tone: "ok", icon: CirclePlay },
  "subscription.cancelled": { title: "إلغاء اشتراك مالك", category: "اشتراكات", tone: "bad", icon: Ban },
  "subscription.invoice_voided": { title: "إلغاء فاتورة", category: "اشتراكات", tone: "bad", icon: ReceiptText },
  "subscription.plan_created": { title: "إنشاء باقة جديدة", category: "اشتراكات", tone: "ok", icon: CirclePlus },
  "subscription.plan_made_default": { title: "تعيين باقة افتراضية", category: "اشتراكات", tone: "navy", icon: Star },
  "subscription.plan_status_changed": { title: "تغيير حالة باقة", category: "اشتراكات", tone: "warn", icon: RefreshCw },
  "subscription.plan_updated": { title: "تعديل بيانات باقة", category: "اشتراكات", tone: "navy", icon: Pencil },

  // طلبات الباقات
  "subscription_request.approved": { title: "الموافقة على طلب باقة", category: "طلبات باقات", tone: "ok", icon: CircleCheck },
  "subscription_request.replied": { title: "الرد على طلب باقة", category: "طلبات باقات", tone: "navy", icon: MessageSquare },
  "subscription_request.reported_payment": { title: "إرفاق إيصال دفع لباقة", category: "طلبات باقات", tone: "navy", icon: ReceiptText },
  "subscription_request.rejected": { title: "رفض طلب باقة", category: "طلبات باقات", tone: "bad", icon: CircleX },

  // الملاعب والاعتمادات
  "Venue.Approval.Approve": { title: "اعتماد وقبول ملعب", category: "ملاعب", tone: "ok", icon: Pitch },
  "Venue.Approval.Reject": { title: "رفض طلب اعتماد ملعب", category: "ملاعب", tone: "bad", icon: CircleX },
  "Venue.Approval.RequestInfo": { title: "طلب استكمال بيانات ملعب", category: "ملاعب", tone: "warn", icon: Info },
  "Venue.SubmittedForApproval": { title: "تقديم ملعب للاعتماد", category: "ملاعب", tone: "navy", icon: MailPlus },
  "admin.venue.info_requested": { title: "طلب معلومات من مالك الملعب", category: "ملاعب", tone: "warn", icon: CircleHelp },
  "admin.venue.note_updated": { title: "تحديث ملاحظة إدارية لملعب", category: "ملاعب", tone: "navy", icon: NotebookPen },
  "admin.venue.ownership_transferred": { title: "نقل ملكية ملعب لمالك جديد", category: "ملاعب", tone: "warn", icon: ArrowLeftRight },
  "PriceRuleCreated": { title: "إنشاء قاعدة تسعير", category: "ملاعب", tone: "ok", icon: Banknote },
  "PriceRuleUpdated": { title: "تحديث قاعدة تسعير", category: "ملاعب", tone: "navy", icon: Banknote },
  "BookingPolicy.VersionCreated": { title: "تحديث سياسة حجز وإلغاء", category: "ملاعب", tone: "navy", icon: ClipboardList },
  "PaymentMethod.Created": { title: "إضافة وسيلة دفع لملعب", category: "ملاعب", tone: "ok", icon: CreditCard },
  "PaymentMethod.DetailsRevealedToPlayer": { title: "كشف بيانات الدفع للاعب", category: "ملاعب", tone: "navy", icon: Eye },
  "Staff.Invited": { title: "إرسال دعوة موظف ملعب", category: "ملاعب", tone: "navy", icon: Mail },

  // حسابات الإدارة والمستخدمين
  "admin.account.created": { title: "إنشاء حساب موظف جديد", category: "حسابات", tone: "ok", icon: User },
  "admin.account.disabled": { title: "تعطيل حساب موظف", category: "حسابات", tone: "bad", icon: Lock },
  "admin.account.enabled": { title: "تفعيل حساب موظف", category: "حسابات", tone: "ok", icon: LockOpen },
  "admin.account.roles_replaced": { title: "تعديل صلاحيات موظف", category: "حسابات", tone: "warn", icon: ShieldCheck },
  "admin.user.viewed": { title: "معاينة ملف مستخدم", category: "مستخدمين", tone: "neutral", icon: Eye },
  "admin.user.suspended": { title: "إيقاف حساب مستخدم", category: "مستخدمين", tone: "bad", icon: Ban },
  "admin.user.reactivated": { title: "إعادة تنشيط حساب مستخدم", category: "مستخدمين", tone: "ok", icon: CircleCheck },
  "admin.user.sessions_revoked": { title: "تسجيل خروج من كل الأجهزة", category: "مستخدمين", tone: "warn", icon: LogOut },
  "admin.impersonation.code_issued": { title: "إصدار رمز دخول نيابة عن مستخدم", category: "حسابات", tone: "warn", icon: KeyRound },
  "admin.impersonation.started": { title: "بدء جلسة دخول نيابة عن مستخدم", category: "حسابات", tone: "warn", icon: VenetianMask },

  // المنصة والحجوزات والدعم
  "platform.setting_changed": { title: "تعديل في إعدادات المنصة", category: "إعدادات", tone: "warn", icon: Settings },
  "platform.flag_changed": { title: "تعديل في خصائص المنصة", category: "إعدادات", tone: "warn", icon: Flag },
  "admin.booking.force_cancelled": { title: "إلغاء حجز إدارياً", category: "حجوزات", tone: "bad", icon: Ban },
  "admin.dispute.resolved": { title: "حل نزاع إيصال دفع", category: "نزاعات", tone: "ok", icon: Scale },
  "dispute.opened": { title: "فتح نزاع إيصال دفع", category: "نزاعات", tone: "warn", icon: TriangleAlert },
  "support.ticket.closed": { title: "إغلاق تذكرة دعم فني", category: "دعم", tone: "neutral", icon: Lock },
  "admin.match.result_corrected": { title: "تصحيح نتيجة مباراة", category: "مباريات", tone: "warn", icon: SoccerBall },
};

const ENTITY_LABELS: Record<string, { label: string; icon: ActionMeta["icon"] }> = {
  Venue: { label: "ملعب", icon: Pitch },
  Subscription: { label: "اشتراك", icon: CreditCard },
  SubscriptionRequest: { label: "طلب باقة", icon: MailPlus },
  SubscriptionPlan: { label: "باقة اشتراك", icon: Package },
  User: { label: "مستخدم/موظف", icon: User },
  Booking: { label: "حجز", icon: Ticket },
  PlatformSetting: { label: "إعداد منصة", icon: Settings },
  SupportTicket: { label: "تذكرة دعم", icon: LifeBuoy },
  PriceRule: { label: "قاعدة تسعير", icon: Banknote },
  PaymentDispute: { label: "نزاع دفع", icon: Scale },
  VenuePolicy: { label: "سياسة حجز", icon: ClipboardList },
  VenuePaymentMethod: { label: "وسيلة دفع", icon: CreditCard },
  VenueStaffInvitation: { label: "دعوة موظف", icon: Mail },
  Invoice: { label: "فاتورة", icon: ReceiptText },
  FeatureFlag: { label: "ميزة تجريبية", icon: Flag },
};

const ACTOR_LABELS: Record<string, { label: string; tone: "navy" | "ok" | "neutral" }> = {
  Admin: { label: "مسؤول نظام", tone: "navy" },
  User: { label: "مستخدم", tone: "ok" },
  System: { label: "النظام تلقائياً", tone: "neutral" },
};

const ACTION_FILTER_OPTIONS: [string, string][] = [
  ["", "كل العمليات والإجراءات"],
  ["subscription", "الاشتراكات وتجديد الباقات"],
  ["subscription_request", "طلبات ترقية الباقات"],
  ["Venue.Approval", "اعتمادات الملاعب وموافقاتها"],
  ["Venue", "عمليات الملاعب وتحديثاتها"],
  ["admin.account", "حسابات الموظفين والصلاحيات"],
  ["admin.user", "إدارة المستخدمين والعملاء"],
  ["platform", "إعدادات المنصة وسياساتها"],
  ["admin.booking", "إدارة الحجوزات والإلغاء"],
  ["dispute", "نزاعات إيصالات الدفع"],
  ["support", "تذاكر الدعم الفني"],
];

const ENTITY_FILTER_OPTIONS: [string, string][] = [
  ["", "كل أنواع الكيانات"],
  ["Venue", "الملاعب"],
  ["Subscription", "الاشتراكات"],
  ["SubscriptionRequest", "طلبات الترقية"],
  ["SubscriptionPlan", "باقات الاشتراك"],
  ["User", "المستخدمين والموظفين"],
  ["Booking", "الحجوزات"],
  ["PlatformSetting", "إعدادات المنصة"],
  ["SupportTicket", "تذاكر الدعم"],
  ["PriceRule", "قواعد التسعير"],
  ["PaymentDispute", "نزاعات الدفع"],
];

const ACTOR_FILTER_OPTIONS: [string, string][] = [
  ["", "كل المنفّذين"],
  ["Admin", "مسؤولي النظام (Admin)"],
  ["User", "المستخدمين (User)"],
];

export default function AuditPage() {
  const [actionCategory, setActionCategory] = useState("");
  const [entityType, setEntityType] = useState("");
  const [entityId, setEntityId] = useState("");
  const [actorType, setActorType] = useState("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<Row | null>(null);

  const logs = useQuery({
    queryKey: ["audit", actionCategory, entityType, entityId, page],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/admin/audit-logs", {
          params: {
            query: {
              action: actionCategory || undefined,
              entityType: entityType || undefined,
              entityId: entityId.trim() || undefined,
              page,
              pageSize: PAGE_SIZE,
            },
          },
        }),
      ),
  });

  const resetFilters = () => {
    setActionCategory("");
    setEntityType("");
    setEntityId("");
    setActorType("");
    setSearch("");
    setPage(1);
  };

  const hasFilters = Boolean(actionCategory || entityType || entityId || actorType || search);

  const filterRows = (items: Row[]) => {
    let res = items;
    if (actorType) {
      res = res.filter((r) => r.actorType.toLowerCase() === actorType.toLowerCase());
    }
    if (search.trim()) {
      const q = search.trim().toLowerCase();
      res = res.filter((r) => {
        const meta = ACTION_MAP[r.action];
        const entity = ENTITY_LABELS[r.entityType];
        const matchAction = (meta?.title || "").toLowerCase().includes(q) || r.action.toLowerCase().includes(q);
        const matchEntity = (entity?.label || "").toLowerCase().includes(q) || r.entityId.toLowerCase().includes(q);
        const matchReason = (r.reason || "").toLowerCase().includes(q);
        const matchActor = (r.actorId || "").toLowerCase().includes(q);
        return matchAction || matchEntity || matchReason || matchActor;
      });
    }
    return res;
  };

  return (
    <Page>
      <PageHeader
        title="سجل التدقيق والعمليات"
        sub="سجل تاريخي وأمني غير قابل للتعديل أو المسح، يوثّق جميع العمليات الإدارية وتغييرات النظام مع الحفاظ على سرية البيانات الحساسة."
      />

      {/* Modern Friendly Filter Bar */}
      <div className="bg-white rounded-2xl p-4 border border-[#EEF1F6] shadow-xs flex flex-col gap-3">
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-2.5">
          <Field label="نوع العملية / الإجراء">
            <Select
              label="نوع العملية"
              value={actionCategory}
              onChange={(v) => {
                setActionCategory(v);
                setPage(1);
              }}
              options={ACTION_FILTER_OPTIONS}
            />
          </Field>

          <Field label="نوع الكيان المتأثر">
            <Select
              label="نوع الكيان"
              value={entityType}
              onChange={(v) => {
                setEntityType(v);
                setPage(1);
              }}
              options={ENTITY_FILTER_OPTIONS}
            />
          </Field>

          <Field label="جهة التنفيذ">
            <Select
              label="جهة التنفيذ"
              value={actorType}
              onChange={(v) => {
                setActorType(v);
                setPage(1);
              }}
              options={ACTOR_FILTER_OPTIONS}
            />
          </Field>

          <Field label="بحث سريع في السجل">
            <TextInput
              placeholder="ابحث بالسبب، كود الكيان، أو العملية..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </Field>
        </div>

        {(entityId || hasFilters) && (
          <div className="flex flex-wrap items-center justify-between gap-2 pt-2 border-t border-slate-100 text-xs">
            <div className="flex items-center gap-2">
              {entityId && (
                <span className="bg-slate-100 text-slate-800 px-2.5 py-1 rounded-lg border border-slate-200 flex items-center gap-1.5 font-medium">
                  <span>تصفية بحسب معرف الكيان:</span>
                  <code dir="ltr" className="font-mono text-slate-900 font-bold">{entityId}</code>
                  <button
                    type="button"
                    onClick={() => setEntityId("")}
                    className="text-slate-600 hover:text-red-600 font-bold ms-1"
                    title="إزالة التصفية"
                  >
                    ×
                  </button>
                </span>
              )}
            </div>

            {hasFilters && (
              <button
                type="button"
                className="text-brand-red hover:underline font-semibold cursor-pointer ms-auto"
                onClick={resetFilters}
              >
                مسح كل الفلاتر
              </button>
            )}
          </div>
        )}
      </div>

      {/* Audit Log Results */}
      <QueryState
        query={logs}
        isEmpty={(d) => filterRows(d.items).length === 0}
        emptyTitle="لا توجد عمليات مسجلة"
        emptyDescription="لم نتمكن من العثور على أي عمليات في سجل التدقيق تطابق الفلاتر المحددة حالياً."
        emptyIcon="shield"
        emptyAction={
          hasFilters
            ? {
                label: "إعادة ضبط الفلاتر",
                onClick: resetFilters,
                variant: "outline",
              }
            : undefined
        }
      >
        {(data) => {
          const visibleRows = filterRows(data.items);
          return (
            <>
              <Table<Row>
                caption="سجل العمليات والتدقيق"
                rows={visibleRows}
                rowKey={(r) => r.id}
                selected={selected?.id}
                onSelect={setSelected}
                columns={[
                  {
                    title: "الوقت والتاريخ",
                    cell: (r) => (
                      <span className="text-xs text-slate-700 font-medium whitespace-nowrap">
                        {fmtDateTime(r.occurredAtUtc)}
                      </span>
                    ),
                  },
                  {
                    title: "العملية / الإجراء",
                    cell: (r) => {
                      const meta = ACTION_MAP[r.action] || {
                        title: r.action,
                        category: "عام",
                        tone: "neutral" as const,
                        icon: Zap,
                      };
                      return (
                        <div className="flex items-center gap-2.5">
                          <span className={`grid place-items-center size-8 rounded-lg shrink-0 ${TONE_ICON[meta.tone]}`}>
                            <meta.icon className="size-4" />
                          </span>
                          <div className="flex flex-col min-w-0">
                            <span className="font-bold text-slate-900 text-[13px]">{meta.title}</span>
                            <span dir="ltr" className="text-[10px] font-mono text-slate-600 truncate">
                              {r.action}
                            </span>
                          </div>
                        </div>
                      );
                    },
                  },
                  {
                    title: "الكيان المتأثر",
                    cell: (r) => {
                      const entity = ENTITY_LABELS[r.entityType] || { label: r.entityType, icon: FileText };
                      return (
                        <div className="flex items-center gap-1.5 whitespace-nowrap">
                          <entity.icon className="size-3.5 text-slate-500" />
                          <span className="font-bold text-slate-800 text-xs">{entity.label}</span>
                          <span dir="ltr" className="text-[10px] font-mono text-slate-600 bg-slate-100 px-1.5 py-0.5 rounded border border-slate-200">
                            {r.entityId.slice(0, 8)}
                          </span>
                        </div>
                      );
                    },
                  },
                  {
                    title: "المنفّذ",
                    cell: (r) => {
                      const actor = ACTOR_LABELS[r.actorType] || { label: r.actorType, tone: "neutral" as const };
                      return (
                        <div className="flex flex-col gap-0.5">
                          <Pill tone={actor.tone} className="w-fit text-[11px] font-bold">
                            {actor.label}
                          </Pill>
                          {r.impersonatorId && (
                            <span className="text-[10px] text-amber-800 font-semibold bg-amber-50 px-1.5 py-0.5 rounded border border-amber-200">
                              بصفة دخول
                            </span>
                          )}
                        </div>
                      );
                    },
                  },
                  {
                    title: "السبب / الملاحظة",
                    cell: (r) => (
                      <span className="line-clamp-2 text-xs text-slate-700 max-w-xs" title={r.reason ?? ""}>
                        {r.reason ? `«${r.reason}»` : <span className={muted}>—</span>}
                      </span>
                    ),
                  },
                  {
                    title: "التفاصيل",
                    cell: (r) => (
                      <button
                        type="button"
                        className={`${btnSmall} text-slate-700 hover:bg-slate-100 font-medium`}
                        onClick={(e) => {
                          e.stopPropagation();
                          setSelected(r);
                        }}
                      >
                        معاينة<ChevronLeft className="size-3.5 inline-block align-[-3px] ms-1" />
                      </button>
                    ),
                  },
                ]}
              />
              <Pager page={page} pageSize={PAGE_SIZE} total={data.totalCount} onChange={setPage} />
            </>
          );
        }}
      </QueryState>

      {/* Human-friendly Details Drawer */}
      <Drawer title="تفاصيل العملية المسجلة" open={!!selected} onClose={() => setSelected(null)}>
        {selected && (
          <div className="flex flex-col gap-4 text-sm">
            {/* Header Card */}
            {(() => {
              const meta = ACTION_MAP[selected.action] || {
                title: selected.action,
                category: "عام",
                tone: "neutral" as const,
                icon: Zap,
              };
              const entity = ENTITY_LABELS[selected.entityType] || { label: selected.entityType, icon: FileText };
              const actor = ACTOR_LABELS[selected.actorType] || { label: selected.actorType, tone: "neutral" as const };

              return (
                <div className={`${card} rounded-2xl p-4 flex flex-col gap-3 border border-slate-200`}>
                  <div className="flex items-center gap-3">
                    <span className={`grid place-items-center size-12 rounded-xl shrink-0 ${TONE_ICON[meta.tone]}`}>
                      <meta.icon className="size-6" />
                    </span>
                    <div className="flex flex-col">
                      <h3 className="font-extrabold text-base text-slate-900">{meta.title}</h3>
                      <code dir="ltr" className="text-[11px] text-slate-600 font-mono">
                        {selected.action}
                      </code>
                    </div>
                  </div>

                  <div className="grid grid-cols-2 gap-2 pt-2 border-t border-slate-100 text-xs">
                    <div>
                      <span className="text-[#5B6475] flex items-center gap-1"><CalendarDays className="size-3" /> التوقيت:</span>
                      <span className="font-bold text-slate-800">{fmtDateTime(selected.occurredAtUtc)}</span>
                    </div>
                    <div>
                      <span className="text-[#5B6475] flex items-center gap-1"><Globe className="size-3" /> عنوان IP:</span>
                      <span dir="ltr" className="font-mono text-slate-800">{selected.ipAddress || "—"}</span>
                    </div>
                    <div>
                      <span className="text-[#5B6475] flex items-center gap-1"><User className="size-3" /> جهة التنفيذ:</span>
                      <Pill tone={actor.tone} className="mt-0.5 text-[11px]">{actor.label}</Pill>
                    </div>
                    <div>
                      <span className="text-[#5B6475] flex items-center gap-1"><Target className="size-3" /> الكيان المتأثر:</span>
                      <span className="font-bold text-slate-800 flex items-center gap-1 mt-0.5">
                        <entity.icon className="size-3.5 text-slate-500" /> {entity.label}
                      </span>
                    </div>
                  </div>

                  {selected.actorId && (
                    <div className="text-xs pt-2 border-t border-slate-100">
                      <span className="text-[#5B6475]">معرّف المنفّذ: </span>
                      <code dir="ltr" className="font-mono text-slate-700 bg-slate-100 px-1.5 py-0.5 rounded text-[11px]">
                        {selected.actorId}
                      </code>
                    </div>
                  )}

                  {selected.impersonatorId && (
                    <div className="text-xs p-2 bg-amber-50 rounded-xl border border-amber-200 text-amber-900">
                      <TriangleAlert className="size-3.5 inline-block align-[-2px] me-1" />تمت هذه العملية عبر جلسة تقمص هوية (Impersonation) بواسطة المشرف:
                      <code dir="ltr" className="font-mono font-bold block mt-1">{selected.impersonatorId}</code>
                    </div>
                  )}

                  {selected.reason && (
                    <div className="pt-2 border-t border-slate-100">
                      <span className="text-xs text-[#5B6475] block mb-1">السبب المسجل للعملية:</span>
                      <p className="bg-[#F8FAFC] rounded-xl p-3 text-slate-900 font-medium border border-slate-200 text-xs">
                        «{selected.reason}»
                      </p>
                    </div>
                  )}
                </div>
              );
            })()}

            {/* Changes / Before & After Details */}
            <JsonDiffViewer before={selected.beforeJson} after={selected.afterJson} />

            {/* Entity Filter Action */}
            <div className="pt-2 flex flex-col gap-2">
              <button
                type="button"
                className={`${btnOutline} text-xs`}
                onClick={() => {
                  setEntityId(selected.entityId);
                  setEntityType(selected.entityType);
                  setSelected(null);
                }}
              >
                <Search className="size-3.5 me-1.5" />
                عرض جميع العمليات الخاصة بهذا الكيان فقط
              </button>
            </div>
          </div>
        )}
      </Drawer>
    </Page>
  );
}

function parseJsonSafe(val?: string | null): Record<string, unknown> | null {
  if (!val) return null;
  try {
    const parsed = JSON.parse(val);
    if (typeof parsed === "object" && parsed !== null) return parsed as Record<string, unknown>;
    return null;
  } catch {
    return null;
  }
}

function JsonDiffViewer({ before, after }: { before?: string | null; after?: string | null }) {
  const beforeObj = parseJsonSafe(before);
  const afterObj = parseJsonSafe(after);
  const [showRaw, setShowRaw] = useState(false);

  if (!before && !after) {
    return (
      <div className="p-3 bg-slate-50 rounded-xl text-xs text-slate-600 border border-slate-100">
        لا توجد تفاصيل إضافية مسجلة في هذا الحدث.
      </div>
    );
  }

  const allKeys = Array.from(
    new Set([...(beforeObj ? Object.keys(beforeObj) : []), ...(afterObj ? Object.keys(afterObj) : [])])
  );

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center justify-between">
        <span className="font-bold text-xs text-slate-800">بيانات التغيير (قبل / بعد):</span>
        <button
          type="button"
          className="text-xs text-brand-red hover:underline font-bold cursor-pointer"
          onClick={() => setShowRaw(!showRaw)}
        >
          {showRaw ? "عرض الجدول المنسق" : "عرض كود JSON"}
        </button>
      </div>

      {showRaw ? (
        <div className="flex flex-col gap-2 text-xs">
          {before && (
            <div className={`${card} rounded-xl p-3 bg-slate-50 border-slate-200`}>
              <strong className="text-xs text-slate-700 block mb-1">البيانات السابقة (قبل):</strong>
              <pre dir="ltr" className="text-[11px] font-mono whitespace-pre-wrap break-all text-slate-800">
                {JSON.stringify(beforeObj || before, null, 2)}
              </pre>
            </div>
          )}
          {after && (
            <div className={`${card} rounded-xl p-3 bg-slate-50 border-slate-200`}>
              <strong className="text-xs text-slate-700 block mb-1">البيانات الحالية (بعد):</strong>
              <pre dir="ltr" className="text-[11px] font-mono whitespace-pre-wrap break-all text-slate-800">
                {JSON.stringify(afterObj || after, null, 2)}
              </pre>
            </div>
          )}
        </div>
      ) : allKeys.length > 0 ? (
        <div className="overflow-hidden rounded-xl border border-slate-200 text-xs">
          <table className="w-full text-right border-collapse">
            <thead>
              <tr className="bg-slate-100 text-slate-700 font-bold border-b border-slate-200">
                <th className="p-2.5">البيان / الحقل</th>
                {beforeObj && <th className="p-2.5">قبل التعديل</th>}
                <th className="p-2.5">القيمة الجديدة</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 bg-white">
              {allKeys.map((key) => {
                const bVal = beforeObj ? formatVal(beforeObj[key]) : null;
                const aVal = afterObj ? formatVal(afterObj[key]) : null;
                const isChanged = bVal !== null && aVal !== null && bVal !== aVal;
                return (
                  <tr key={key} className={isChanged ? "bg-amber-50/60" : ""}>
                    <td className="p-2.5 font-bold text-slate-700 font-mono" dir="ltr">
                      {key}
                    </td>
                    {beforeObj && (
                      <td className="p-2.5 text-slate-600 font-mono" dir="ltr">
                        {bVal}
                      </td>
                    )}
                    <td className={`p-2.5 font-mono ${isChanged ? "font-bold text-emerald-800" : "text-slate-800"}`} dir="ltr">
                      {aVal ?? "—"}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      ) : (
        <div className="p-3 bg-slate-50 rounded-xl text-xs text-slate-700 font-mono break-all" dir="ltr">
          {after || before}
        </div>
      )}
    </div>
  );
}

function formatVal(v: unknown): string {
  if (v === null || v === undefined) return "—";
  if (typeof v === "object") return JSON.stringify(v);
  if (typeof v === "boolean") return v ? "نعم (True)" : "لا (False)";
  return String(v);
}
