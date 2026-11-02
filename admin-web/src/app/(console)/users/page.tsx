"use client";

import Link from "next/link";
import { RequireArea } from "@/components/console";
import { FilterBar, SearchFilter } from "@/components/filters";
import { EmptyState, ErrorPanel, Loading, PageHeader, Panel, TableScroll } from "@/components/ui";
import { day, num, stamp } from "@/lib/format";
import type { UserRow } from "@/lib/types";
import { qs, useApi } from "@/lib/use-api";
import { useUrlFilters } from "@/lib/use-filters";

const KEYS = ["q"] as const;

export default function UsersPage() {
  return (
    <RequireArea area="users">
      <Users />
    </RequireArea>
  );
}

function Users() {
  const f = useUrlFilters(KEYS);
  const q = f.values.q.trim();
  const ready = q.length >= 2;
  const { data, error, reload } = useApi<UserRow[]>(ready ? `/api/platform/users${qs({ q })}` : null);

  return (
    <>
      <PageHeader title="Users" subtitle="People with an account on any product: owners, staff, learners, seekers and guests." />
      <FilterBar>
        <SearchFilter label="Search" placeholder="Email, name or phone (at least 2 characters)" value={f.values.q} onCommit={(v) => f.set({ q: v })} minLength={2} />
      </FilterBar>
      <Panel flush>
        {!ready && <EmptyState title="Search for a person" text="Type at least 2 characters of an email address, name or phone number. Up to 50 matches are shown." />}
        {ready && error && <ErrorPanel error={error} onRetry={reload} className="m-3" />}
        {ready && !data && !error && <Loading />}
        {ready && data && data.length === 0 && <EmptyState title={`No one matches "${q}"`} text="Try the full email address, or a phone number with its country code." />}
        {ready && data && data.length > 0 && (
          <TableScroll label="Users">
            <table className="data-table min-w-[720px]">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Email</th>
                  <th className="num">Businesses</th>
                  <th className="num">Joined</th>
                  <th className="num">Last sign-in</th>
                </tr>
              </thead>
              <tbody>
                {data.map((u) => (
                  <tr key={u.id} className="row-link">
                    <td>
                      <Link href={`/users/${u.id}`} className="font-medium underline decoration-line underline-offset-2 hover:decoration-graphite">
                        {u.name || "(no name)"}
                      </Link>
                    </td>
                    <td>{u.email}</td>
                    <td className="num">{num(u.businesses)}</td>
                    <td className="num">{day(u.createdAt)}</td>
                    <td className="num">{stamp(u.lastLoginAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {data.length >= 50 && <p className="border-t border-line px-4 py-2 text-xs text-muted">Showing the first 50 matches. Narrow the search to find someone else.</p>}
          </TableScroll>
        )}
      </Panel>
    </>
  );
}
