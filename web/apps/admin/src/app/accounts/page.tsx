"use client";

import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Schemas } from "@malabak/api-client";
import { Page, PageHeader, Pill, ReasonModal, type ReasonRequest, btnDanger, btnOutline, btnPrimary, btnSmall, card, muted } from "@/components/ui";
import { Drawer, Field, QueryState, Section, Table, TextInput, fmtDate, useNotice } from "@/components/admin";
import { api, unwrap } from "@/lib/api";
import { useStepUp } from "@/lib/stepup";

type Account = Schemas["AdminAccountDto"];
const STAFF_ROLES: [string, string][] = [["Admin", "أدمن (كل الصلاحيات)"], ["SupportAgent", "دعم"], ["Finance", "مالية"]];

export default function AccountsPage() {
  const queryClient = useQueryClient();
  const stepUp = useStepUp();
  const notice = useNotice();
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<Account | null>(null);
  const [reason, setReason] = useState<ReasonRequest | null>(null);

  const accounts = useQuery({ queryKey: ["admin-accounts"], queryFn: () => unwrap(api.GET("/api/v1/admin/accounts")) });
  const roles = useQuery({ queryKey: ["admin-roles"], queryFn: () => unwrap(api.GET("/api/v1/admin/roles")) });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ["admin-accounts"] });

  const toggle = useMutation({
    mutationFn: ({ account, text }: { account: Account; text: string }) =>
      account.status === "Suspended"
        ? unwrap(api.POST("/api/v1/admin/accounts/{accountId}/enable", { params: { path: { accountId: account.id } }, body: { reason: text } }))
        : stepUp((headers) => unwrap(api.POST("/api/v1/admin/accounts/{accountId}/disable", { params: { path: { accountId: account.id } }, headers, body: { reason: text } }))),
    onSuccess: async (a) => { notice.ok(a.status === "Suspended" ? "اتقفل الحساب وجلساته." : "الحساب رجع يشتغل."); await refresh(); },
    onError: notice.fail,
  });

  return (
    <Page>
      <PageHeader title="حسابات الفريق" sub="الحسابات الجديدة بتاخد رابط تعيين كلمة سر، ولازم تفعّل التحقق بخطوتين أول دخول. آخر أدمن نشط مايتشالش.">
        <button type="button" className={btnPrimary} onClick={() => setCreating(true)}>حساب جديد</button>
      </PageHeader>
      {notice.node}
      <QueryState query={accounts} isEmpty={(d) => d.length === 0} empty="مفيش حسابات.">
        {(list) => (
          <Table<Account>
            caption="حسابات الفريق"
            rows={list}
            rowKey={(a) => a.id}
            columns={[
              { title: "الاسم", cell: (a) => <strong>{a.fullName}</strong> },
              { title: "الإيميل", cell: (a) => <span dir="ltr" className="font-inter text-xs">{a.email}</span> },
              { title: "الأدوار", cell: (a) => a.roles.filter((r) => STAFF_ROLES.some(([k]) => k === r)).join("، ") },
              { title: "2FA", cell: (a) => (a.twoFactorEnabled ? "مفعّل" : <Pill tone="warn">لسه</Pill>) },
              { title: "الحالة", cell: (a) => <Pill tone={a.status === "Active" ? "ok" : "bad"}>{a.status}</Pill> },
              { title: "الإضافة", cell: (a) => fmtDate(a.createdAtUtc) },
              {
                title: "",
                cell: (a) => (
                  <div className="flex gap-1">
                    <button type="button" className={btnSmall} onClick={() => setEditing(a)}>الأدوار</button>
                    <button
                      type="button"
                      className={btnSmall}
                      onClick={() =>
                        setReason({
                          title: a.status === "Suspended" ? "تفعيل الحساب" : "تعطيل الحساب",
                          body: a.status === "Suspended" ? "" : "كل جلساته هتتقفل فورًا.",
                          confirmLabel: a.status === "Suspended" ? "تفعيل" : "تعطيل",
                          onConfirm: (text) => toggle.mutate({ account: a, text }),
                        })
                      }
                    >
                      {a.status === "Suspended" ? "تفعيل" : "تعطيل"}
                    </button>
                  </div>
                ),
              },
            ]}
          />
        )}
      </QueryState>
      <Section title="الأدوار والصلاحيات">
        <QueryState query={roles}>
          {(list) => (
            <div className="grid md:grid-cols-3 gap-3">
              {list.filter((r) => r.isStaffRole).map((r) => (
                <div key={r.role} className={`${card} rounded-[16px] p-3 flex flex-col gap-1`}>
                  <strong>{STAFF_ROLES.find(([k]) => k === r.role)?.[1] ?? r.role}</strong>
                  {r.permissions.map((p) => (
                    <span key={p} dir="ltr" className={`text-[11px] font-inter text-right ${muted}`}>{p}</span>
                  ))}
                </div>
              ))}
            </div>
          )}
        </QueryState>
      </Section>
      {creating && <CreateAccount onClose={() => setCreating(false)} onDone={async () => { setCreating(false); notice.ok("اتعمل الحساب ووصله رابط تعيين كلمة السر."); await refresh(); }} />}
      {editing && <EditRoles account={editing} onClose={() => setEditing(null)} onDone={async () => { setEditing(null); notice.ok("اتغيرت الأدوار وجلساته اتقفلت."); await refresh(); }} />}
      <ReasonModal request={reason} onClose={() => setReason(null)} />
    </Page>
  );
}

function RolePicker({ value, onChange }: { value: string[]; onChange: (v: string[]) => void }) {
  return (
    <fieldset className="flex flex-col gap-1.5">
      <legend className="text-[13px] font-semibold mb-1">الأدوار</legend>
      {STAFF_ROLES.map(([role, title]) => (
        <label key={role} className="flex gap-2 text-sm items-center">
          <input type="checkbox" checked={value.includes(role)} onChange={(e) => onChange(e.target.checked ? [...value, role] : value.filter((r) => r !== role))} />
          {title}
        </label>
      ))}
    </fieldset>
  );
}

function CreateAccount({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const stepUp = useStepUp();
  const notice = useNotice();
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [roles, setRoles] = useState<string[]>(["SupportAgent"]);
  const create = useMutation({
    mutationFn: () => stepUp((headers) => unwrap(api.POST("/api/v1/admin/accounts", { headers, body: { fullName: name, email, phoneNumber: null, roles } }))),
    onSuccess: onDone,
    onError: notice.fail,
  });
  return (
    <Drawer title="حساب جديد" open onClose={onClose}>
      {notice.node}
      <Field label="الاسم"><TextInput value={name} onChange={(e) => setName(e.target.value)} /></Field>
      <Field label="الإيميل"><TextInput dir="ltr" type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></Field>
      <RolePicker value={roles} onChange={setRoles} />
      <div className="flex gap-2 justify-end">
        <button type="button" className={btnOutline} onClick={onClose}>رجوع</button>
        <button type="button" className={btnPrimary} disabled={!name.trim() || !email.includes("@") || roles.length === 0 || create.isPending} onClick={() => create.mutate()}>إنشاء</button>
      </div>
    </Drawer>
  );
}

function EditRoles({ account, onClose, onDone }: { account: Account; onClose: () => void; onDone: () => void }) {
  const stepUp = useStepUp();
  const notice = useNotice();
  const [roles, setRoles] = useState<string[]>(account.roles.filter((r) => STAFF_ROLES.some(([k]) => k === r)));
  const [why, setWhy] = useState("");
  const save = useMutation({
    mutationFn: () =>
      stepUp((headers) => unwrap(api.PUT("/api/v1/admin/accounts/{accountId}/roles", { params: { path: { accountId: account.id } }, headers, body: { roles, reason: why } }))),
    onSuccess: onDone,
    onError: notice.fail,
  });
  return (
    <Drawer title={`أدوار ${account.fullName}`} open onClose={onClose}>
      {notice.node}
      <RolePicker value={roles} onChange={setRoles} />
      <Field label="السبب (بيتسجل في التدقيق)"><TextInput value={why} onChange={(e) => setWhy(e.target.value)} /></Field>
      <div className="flex gap-2 justify-end">
        <button type="button" className={btnOutline} onClick={onClose}>رجوع</button>
        <button type="button" className={roles.includes("Admin") ? btnPrimary : btnDanger} disabled={roles.length === 0 || why.trim().length < 5 || save.isPending} onClick={() => save.mutate()}>حفظ</button>
      </div>
    </Drawer>
  );
}
