import React from "react";

export const Table: React.FC<React.HTMLAttributes<HTMLTableElement>> = ({
  className = "",
  children,
  ...props
}) => (
  <div className="relative w-full overflow-auto">
    <table
      className={`w-full caption-bottom text-sm text-start border-collapse ${className}`}
      {...props}
    >
      {children}
    </table>
  </div>
);

export const TableHeader: React.FC<React.HTMLAttributes<HTMLTableSectionElement>> = ({
  className = "",
  children,
  ...props
}) => (
  <thead className={`border-b border-slate-200 bg-slate-50 dark:border-slate-800 dark:bg-slate-900/50 ${className}`} {...props}>
    {children}
  </thead>
);

export const TableBody: React.FC<React.HTMLAttributes<HTMLTableSectionElement>> = ({
  className = "",
  children,
  ...props
}) => (
  <tbody className={`divide-y divide-slate-200 dark:divide-slate-800 ${className}`} {...props}>
    {children}
  </tbody>
);

export const TableRow: React.FC<React.HTMLAttributes<HTMLTableRowElement>> = ({
  className = "",
  children,
  ...props
}) => (
  <tr
    className={`transition-colors hover:bg-slate-50/50 dark:hover:bg-slate-900/50 ${className}`}
    {...props}
  >
    {children}
  </tr>
);

export const TableHead: React.FC<React.ThHTMLAttributes<HTMLTableCellElement>> = ({
  className = "",
  children,
  ...props
}) => (
  <th
    className={`h-11 px-4 text-start align-middle font-medium text-slate-500 dark:text-slate-400 [&:has([role=checkbox])]:pe-0 ${className}`}
    {...props}
  >
    {children}
  </th>
);

export const TableCell: React.FC<React.TdHTMLAttributes<HTMLTableCellElement>> = ({
  className = "",
  children,
  ...props
}) => (
  <td
    className={`p-4 align-middle text-start text-slate-700 dark:text-slate-300 [&:has([role=checkbox])]:pe-0 ${className}`}
    {...props}
  >
    {children}
  </td>
);
