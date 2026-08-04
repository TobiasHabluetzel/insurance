/* CATEGORY_FLOWS — plain JS port of src/DeGoudse.Web/src/claimFlows.ts.
 * Consumed by the flow-walker directive (and by ClaimSubmittedPage for
 * the category label). Kept 1:1 with the React tree so both frontends
 * stay in step. */
(function (root) {
    'use strict';

    root.__claimFlows = {
        accident: {
            label: 'an accident claim',
            start: 'police',
            nodes: {
                police: { id: 'police', question: 'Was the police involved?', type: 'yesno', next: { yes: 'police_report', no: 'witness' } },
                police_report: { id: 'police_report', question: 'Do you have a police report?', type: 'yesno', next: { yes: 'police_upload', no: 'witness' } },
                police_upload: { id: 'police_upload', question: 'Please upload the police report.', type: 'file', next: { done: 'witness' } },
                witness: { id: 'witness', question: 'Were there any witnesses?', type: 'yesno', next: { yes: 'witness_details', no: 'end' } },
                witness_details: { id: 'witness_details', question: 'Please provide the witness name and contact details.', type: 'text', next: { done: 'end' } },
                end: { id: 'end', type: 'end' },
            },
        },

        medical: {
            label: 'a medical claim',
            start: 'police',
            nodes: {
                police: { id: 'police', question: 'Was the police involved?', type: 'yesno', next: { yes: 'witnesses', no: 'witnesses' } },
                witnesses: { id: 'witnesses', question: 'Were there third-party witnesses?', type: 'yesno', next: { yes: 'end', no: 'end' } },
                end: { id: 'end', type: 'end' },
            },
        },

        travel: {
            label: 'a travel disruption claim',
            start: 'delay',
            nodes: {
                delay: { id: 'delay', question: 'Was your travel delayed?', type: 'yesno', next: { yes: 'delay_hours', no: 'cancellation' } },
                delay_hours: { id: 'delay_hours', question: 'How many hours was the delay?', type: 'text', next: { done: 'delay_cert' } },
                delay_cert: { id: 'delay_cert', question: 'Do you have a delay certificate from the carrier?', type: 'yesno', next: { yes: 'delay_upload', no: 'cancellation' } },
                delay_upload: { id: 'delay_upload', question: 'Please upload the delay certificate.', type: 'file', next: { done: 'cancellation' } },
                cancellation: { id: 'cancellation', question: 'Was your trip fully cancelled?', type: 'yesno', next: { yes: 'cancellation_docs', no: 'end' } },
                cancellation_docs: { id: 'cancellation_docs', question: 'Please upload the cancellation confirmation.', type: 'file', next: { done: 'end' } },
                end: { id: 'end', type: 'end' },
            },
        },

        theft: {
            label: 'a theft or loss claim',
            start: 'reported',
            nodes: {
                reported: { id: 'reported', question: 'Was the theft reported to the police?', type: 'yesno', next: { yes: 'police_report', no: 'items' } },
                police_report: { id: 'police_report', question: 'Do you have a police report?', type: 'yesno', next: { yes: 'police_upload', no: 'items' } },
                police_upload: { id: 'police_upload', question: 'Please upload the police report.', type: 'file', next: { done: 'items' } },
                items: { id: 'items', question: 'Please list the stolen or lost items.', type: 'text', next: { done: 'receipts' } },
                receipts: { id: 'receipts', question: 'Do you have purchase receipts for the items?', type: 'yesno', next: { yes: 'receipts_upload', no: 'end' } },
                receipts_upload: { id: 'receipts_upload', question: 'Please upload the receipts.', type: 'file', next: { done: 'end' } },
                end: { id: 'end', type: 'end' },
            },
        },

        property: {
            label: 'a property damage claim',
            start: 'photos',
            nodes: {
                photos: { id: 'photos', question: 'Do you have photos of the damage?', type: 'yesno', next: { yes: 'photos_upload', no: 'estimate' } },
                photos_upload: { id: 'photos_upload', question: 'Please upload the photos.', type: 'file', next: { done: 'estimate' } },
                estimate: { id: 'estimate', question: 'Do you have a repair estimate?', type: 'yesno', next: { yes: 'estimate_upload', no: 'end' } },
                estimate_upload: { id: 'estimate_upload', question: 'Please upload the repair estimate.', type: 'file', next: { done: 'end' } },
                end: { id: 'end', type: 'end' },
            },
        },

        other: {
            label: 'a claim',
            start: 'end',
            nodes: { end: { id: 'end', type: 'end' } },
        },
    };
})(window);
