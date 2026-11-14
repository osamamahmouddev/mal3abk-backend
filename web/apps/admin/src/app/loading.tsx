export default function Loading() {
  return (
    <div className="flex-1 p-4 sm:p-6 lg:p-8 flex flex-col gap-6 animate-fade-in" dir="rtl">
      {/* Page Header Skeleton */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div className="flex flex-col gap-2">
          <div className="h-7 w-48 rounded-lg skeleton-shimmer" />
          <div className="h-4 w-80 rounded-md skeleton-shimmer" />
        </div>
        <div className="h-10 w-28 rounded-[10px] skeleton-shimmer shrink-0" />
      </div>

      {/* Stats Cards Skeleton */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {[1, 2, 3, 4].map((i) => (
          <div key={i} className="p-4 rounded-xl bg-white border border-[#E4E7EC] shadow-2xs flex flex-col gap-3">
            <div className="flex items-center justify-between">
              <div className="h-4 w-24 rounded skeleton-shimmer" />
              <div className="size-8 rounded-lg skeleton-shimmer" />
            </div>
            <div className="h-7 w-20 rounded-md skeleton-shimmer" />
          </div>
        ))}
      </div>

      {/* Table Skeleton */}
      <div className="p-5 rounded-xl bg-white border border-[#E4E7EC] shadow-2xs flex flex-col gap-3">
        <div className="h-5 w-32 rounded skeleton-shimmer" />
        <div className="h-11 w-full rounded-lg skeleton-shimmer" />
        <div className="h-11 w-full rounded-lg skeleton-shimmer" />
        <div className="h-11 w-full rounded-lg skeleton-shimmer" />
        <div className="h-11 w-full rounded-lg skeleton-shimmer" />
      </div>
    </div>
  );
}
