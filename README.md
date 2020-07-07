# EventRegistration

This Azure Functions where created for making the registration for student events easier.
At the start of an event all the students who registered needed to be searched manually through an excel file, and some time they where not found.
With this API, all the students registering for an event would get an email containing their QR code. This code was later scanned and an excel in onedrive would be edited by a Azure Logic function, marking the student as 'present'.

The function generating the QR codes would get in the body of the request the phone number and the email of the student. Using this values a QR code would be created pointing to the Azure Logic Function API.

The image would be saved in an Azure Storage account as an SVG file.
This file would be send to the email of the sender.
