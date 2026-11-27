using Geef.Atelier.Core.Domain.Research;

namespace Geef.Atelier.Tests.Domain.Research;

/// <summary>
/// <see cref="ResearchTopicPrivacy"/>: topics with an email address, a phone or account number, a date of birth or a street address
/// are recognized, also when the request itself names them; years, year ranges, paragraphs, amounts, norms and dates of this century
/// still go out. All data invented.
/// </summary>
public sealed class ResearchTopicPrivacyTests
{
    [Theory]
    [InlineData("Erika Mustermann Hauptstraße 5 50667 Köln Pflegegrad Widerspruch Frist")]
    [InlineData("Kündigung Mietvertrag Lindenallee 12a")]
    [InlineData("Mieterhöhung Gartenweg 3")]
    [InlineData("Erika Mustermann 50667 Köln Pflegegrad")]
    [InlineData("10 Example Road London council tax")]
    [InlineData("0171 2345678 Erika Mustermann")]
    [InlineData("+49 171 2345678 Rückruf")]
    [InlineData("030/1234567 Hausverwaltung")]
    [InlineData("Kundennummer 12345678 Kündigung")]
    [InlineData("DE89 3704 0044 0532 0130 00 Bank")]
    [InlineData("GB29 NWBK 6016 1331 9268 19 refund")]
    [InlineData("3704 0044 0532 0130 Überweisung")]
    [InlineData("Erika Mustermann geboren 03.04.1951 Pflegegrad")]
    [InlineData("Rente Jahrgang 1958 Abschläge")]
    [InlineData("Max Beispiel 12.07.1985 Versicherung")]
    [InlineData("date of birth 1990-05-17 passport renewal")]
    [InlineData("max.mustermann@example.org Verzugszinsen")]
    [InlineData("erika (at) example.org Antwort")]
    public void Contact_and_account_details_are_recognized(string topic) =>
        Assert.True(topic.HoldsContactDetails(), topic);

    [Theory]
    [InlineData("Mindestlohn 2027 Höhe")]
    [InlineData("Mietpreisbremse Verlängerung 2025-2026")]
    [InlineData("§ 573c BGB Kündigungsfrist Vermieter")]
    [InlineData("Art. 6 Abs. 1 DSGVO Rechtsgrundlage")]
    [InlineData("Grundfreibetrag 2026 12.096 Euro")]
    [InlineData("Förderung bis 1 250 000 Euro Programm")]
    [InlineData("Wohngeld Höchstbetrag 50000 Euro Vermögen")]
    [InlineData("ISO 27001 Zertifizierung Kosten")]
    [InlineData("DIN EN ISO 14001 Umweltmanagement")]
    [InlineData("ISO 9001 2015 Änderungen")]
    [InlineData("DIN 18040-1 barrierefreies Bauen")]
    [InlineData("Frist 31.12.2026 Steuererklärung")]
    [InlineData("70. Geburtstag Feier Ideen 2026")]
    [InlineData("Arbeitsplatz 2026 Ergonomie Vorschriften")]
    [InlineData("Bürgergeld Regelsatz 563 Euro")]
    [InlineData("Paragraph 5 Arbeitszeitgesetz")]
    [InlineData("Deutschlandticket Preis 58 Euro")]
    [InlineData("Stadt mit 12000 Einwohnern Förderprogramm")]
    [InlineData("")]
    public void Years_paragraphs_amounts_norms_and_current_dates_still_go_out(string topic) =>
        Assert.False(topic.HoldsContactDetails(), topic);

    [Fact]
    public void The_gate_withholds_such_a_topic_even_when_the_request_names_the_detail()
    {
        const string request = "Schreib an die Pflegekasse für Erika Mustermann, Hauptstraße 5, 50667 Köln, Telefon 0171 2345678.";

        var selection = ResearchQueryGate.Select(
            ["Erika Mustermann Hauptstraße 5 50667 Köln Pflegegrad", "0171 2345678 Rückruf Pflegekasse", "Pflegegrad Widerspruch Frist"],
            request, [], null, 4);

        Assert.Equal(["Pflegegrad Widerspruch Frist"], selection.Sent);
        Assert.Equal(2, selection.Withheld);
    }
}
