# Malabak.SharedKernel

Ultra-lean domain primitives, value objects, and contracts shared across all modules without taking dependencies on specific modules or external commercial frameworks.

## Core Primitives
- `Result<TValue>` & `Error`: Railway-oriented programming return types eliminating exceptions for control flow.
- `Money`: Immutable value object supporting currency validation (`EGP`), rounding, scaling, and arithmetic operations.
- `PhoneNumber`: Validates and normalizes Egyptian and international numbers to E.164 and local dial patterns using `libphonenumber-csharp`.
- `TimeSlotRange`: Encapsulates temporal boundaries normalized to UTC with overlap detection (`Overlaps`, `Touches`, `Contains`) and 24-hour maximum span limits.
- `IClock` & `SystemClock`: Testable time abstraction providing `UtcNow`, `CairoNow`, and `CairoToday` with automatic Egyptian Daylight Saving Time conversions.
- `ICommand<T>` / `IQuery<T>`: Lightweight mediator handler contracts for CQRS-lite dispatch.
