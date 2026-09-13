import { HeroSection } from '@/features/landing/HeroSection';
import { TrustStrip } from '@/features/landing/TrustStrip';
import { HowItWorksSection } from '@/features/landing/HowItWorksSection';
import { ServicesSection } from '@/features/landing/ServicesSection';
import { FeaturesSection } from '@/features/landing/FeaturesSection';
import { CoverageSection } from '@/features/landing/CoverageSection';
import { CtaSection } from '@/features/landing/CtaSection';
import { useHashScroll } from '@/shared/lib/useHashScroll';

/** Landing page — composes presentation-only sections; no business logic here. */
export function LandingPage() {
  // Every section here fills in after a fetch, so the browser's own handling of "#coverage" and
  // its neighbours fires before the target exists.
  useHashScroll();

  return (
    <div className="w-full">
      <HeroSection />
      <TrustStrip />
      <HowItWorksSection />
      <ServicesSection />
      <CoverageSection />
      <FeaturesSection />
      <CtaSection />
    </div>
  );
}
