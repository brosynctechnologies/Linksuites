import { PhoneFrame } from "@/components/suite/phone";
import { AppointmentCardDrawing, BookingPhoneScreen, Drawing, ReminderDrawing } from "./screens";

/**
 * The landing page's one visual: a booking in three moments. The booking page rises from the stage floor, the
 * appointment card it turns into sits beside it, and the WhatsApp reminder follows. The wet-paint sheen passes over them
 * once, in that order. On phones the card is left out so the booking page and its deposit button stay in view.
 */
export function HeroStage() {
  return (
    <div className="relative h-[31rem] overflow-hidden rounded-ls-panel bg-app-wash sm:h-[26rem]">
      <div className="absolute top-[7.5rem] left-1/2 -translate-x-1/2 sm:top-8 sm:left-8 sm:translate-x-0">
        <PhoneFrame label="BookLink on a phone: Kemi's Hair Studio's booking page, with knotless braids chosen, Saturday 14 November picked, 10:30 selected and a button to pay a ₦5,000 deposit and book.">
          <BookingPhoneScreen />
        </PhoneFrame>
      </div>
      <Drawing
        label="The appointment card once the deposit is paid: 10:30 on Saturday 14 November, knotless braids with Tunde, confirmed."
        className="absolute top-8 right-6 hidden w-[15rem] rotate-[2deg] sm:block"
      >
        <AppointmentCardDrawing paintIndex={1} />
      </Drawing>
      <Drawing
        label="The WhatsApp reminder Kemi's Hair Studio sends Ada the day before, with a link to manage her booking."
        className="absolute top-4 right-3 left-3 sm:top-auto sm:right-6 sm:bottom-6 sm:left-auto sm:w-[15rem]"
      >
        <ReminderDrawing paintIndex={2} />
      </Drawing>
    </div>
  );
}
