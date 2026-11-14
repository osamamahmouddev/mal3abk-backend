export default function Loading() {
  return (
    <div className="flex-1 p-4 sm:p-6 lg:p-8 flex flex-col gap-6 animate-fade-in" dir="rtl">
      {/* Page Header Skeleton */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div className="flex flex-col gap-2">
          <div className="h-7 w-44 rounded-lg skeleton-shimmer" />
          <div className="h-4 w-72 rounded-md skeleton-shimmer" />
        </div>
        <div className="h-10 w-32 rounded-xl skeleton-shimmer shrink-0" />
      </div>

      {/* Metric Cards Skeleton */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {[1, 2, 3, 4].map((i) => (
          <div key={i} className="p-4 rounded-2xl bg-white border border-[#EDF0F5] shadow-2xs flex flex-col gap-3">
            <div className="flex items-center justify-between">
              <div className="h-4 w-20 rounded skeleton-shimmer" />
              <div className="size-8 rounded-lg skeleton-shimmer" />
            </div>
            <div className="h-8 w-28 rounded-lg skeleton-shimmer" />
            <div className="h-3 w-36 rounded skeleton-shimmer" />
          </div>
        ))}
      </div>

      {/* Main Content Area Skeleton */}
      <div className="p-6 rounded-2xl bg-white border border-[#EDF0F5] shadow-2xs flex flex-col gap-4">
        <div className="h-5 w-40 rounded skeleton-shimmer" />
        <div className="h-12 w-full rounded-xl skeleton-shimmer" />
        <div className="h-12 w-full rounded-xl skeleton-shimmer" />
        <div className="h-12 w-full rounded-xl skeleton-shimmer" />
        <div className="h-12 w-full rounded-xl skeleton-shimmer" />
      </div>
    </div>
  );
}
