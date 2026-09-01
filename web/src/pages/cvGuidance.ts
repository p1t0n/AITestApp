/**
 * What we ask people not to put into the CV's free text (P1T-183, Art. 9).
 *
 * A mitigation, not a solution, and the manual says so: nothing stops somebody writing it anyway.
 * Asking is the only honest control available here — classifying the text on save would create the
 * very special-category inference it aims to avoid, and you cannot informedly consent to incidental
 * content you do not know you are about to write.
 *
 * One string, shared by every free-text field, so the ask is identical wherever somebody types
 * rather than three near-miss wordings that read as three different rules.
 */
export const SPECIAL_CATEGORY_GUIDANCE =
  "Please leave out anything about health, religion or beliefs, political opinions, " +
  "trade-union membership, ethnicity, sex life or sexual orientation. The service never " +
  "searches, filters or infers on any of it, and leaving it out is the only way to be sure " +
  "it is not held at all.";
