# Sharpino.Sample.15

This sample demonstrates a new approach to handling enrollments.

## Enrollment Stream

Instead of using two separate streams for enrollments that need to be synchronized, this sample uses a single enrollment stream. This stream contains all enrollment information, linking `studentId` and `courseId`.

The previous approach of embedding enrollment information in both student and course streams was for efficiency and simpler relationship navigation. However, this new design with an independent enrollment stream can offer efficiency thanks to the "detailscache" (a detailed view of multiple objects that can stay in sync reacting to events related to any "dependent object").

## Refreshable Details

The efficiency gain comes from the new `refreshableDetails` feature. This feature maintains a cache of details that is automatically refreshed whenever any of its related streams are updated. For example, the detail view stays consistently in sync when the enrollment stream, the student stream, or the course stream is updated.
