export function PlaceholderPage({ title }: { title: string }) {
  return (
    <section aria-labelledby="placeholder-heading" className="flex flex-col gap-2">
      <h1 id="placeholder-heading" className="text-xl font-semibold tracking-tight">
        {title}
      </h1>
      <p className="text-sm text-muted-foreground">
        This area is delivered with the full web application phase; the API contract behind it already exists.
      </p>
    </section>
  )
}
